using System;
using System.Linq;
using Misk.Domain;
using Misk.Data;

namespace Misk.Presentation;

/// <summary>
/// Host authority + networking surface for <see cref="MiskGame"/>: lobby/seat management,
/// the RPC request handlers clients call, building the authoritative match, and bridging the
/// domain event bus into the [Sync] mirror. Everything here is a no-op on non-authority
/// clients (guarded by <see cref="MiskGame.IsAuthority"/>).
/// </summary>
public sealed partial class MiskGame
{
	// Host-only authority (never networked or serialized).
	private GameState _state;
	private TurnController _controller;
	private SetupController _setupController;
	private GameContext _ctx;
	private GameEventBus _events;
	private IDiceRoller _rng;

	private int MaxSeats => Math.Min( 6, Data?.Factions.Count ?? 6 );

	// ------------------------------------------------------------------ menu entry points
	// Called locally by the UI on the machine that initiates the session.

	/// <summary>Host an online game: create a lobby (if needed) and seat the local player.</summary>
	public void StartOnlineHosting()
	{
		if ( Networking.IsActive && !Networking.IsHost )
			return; // already a guest in someone else's lobby

		if ( !Networking.IsActive )
			Networking.CreateLobby();

		IsHotseat = false;
		Mode = GameMode.Lobby;
		AddSeatForConnection( Connection.Local );
		BumpVersion();
	}

	/// <summary>Find and join the first open Misk lobby (used by the client's "Join a War" button).</summary>
	public async void JoinAny()
	{
		if ( Networking.IsActive )
			return;

		JoinStatus = "Seeking open wars…";
		try
		{
			var lobbies = await Networking.QueryLobbies( "local.misk" );
			if ( lobbies == null || lobbies.Count == 0 )
			{
				JoinStatus = "No open wars found. Have the host press “Host a War” first.";
				return;
			}

			JoinStatus = $"Joining {lobbies[0].Name}…";
			Networking.Connect( lobbies[0].LobbyId );
		}
		catch ( System.Exception e )
		{
			JoinStatus = "Join failed: " + e.Message;
		}
	}

	/// <summary>Start a single-machine hotseat game with the given number of local seats.</summary>
	public void StartHotseatSetup( int seatCount )
	{
		if ( Data == null )
			return;

		IsHotseat = true;
		Mode = GameMode.Lobby;
		Seats.Clear();

		seatCount = Math.Clamp( seatCount, 2, MaxSeats );
		for ( int i = 0; i < seatCount; i++ )
		{
			Seats.Add( new SeatInfo
			{
				PlayerId = $"seat_{i}",
				FactionId = Data.Factions[i % Data.Factions.Count].Id,
				DisplayName = $"Player {i + 1}",
				ConnectionId = Guid.Empty,
				IsReady = true,
				IsHuman = true,
			} );
		}
		BumpVersion();
	}

	/// <summary>Leave the current game/lobby and return everyone to the main menu.</summary>
	public void ReturnToMenu()
	{
		if ( !IsAuthority )
			return;

		Mode = GameMode.MainMenu;
		Seats.Clear();
		TurnOrder.Clear();
		Armies.Clear();
		Owners.Clear();
		CardCounts.Clear();
		SetupRemaining.Clear();
		CombatLog.Clear();
		_handCache.Clear();
		WinnerPlayerId = null;
		PendingReinforcements = 0;
		CurrentPlayerIndex = 0;
		TurnNumber = 1;
		PhaseIndex = 0;
		CardSetsTradedIn = 0;
		InSetup = false;
		SetupStepIndex = 0;
		ClearPendingAdvanceSync();
		_state = null;
		_controller = null;
		_setupController = null;
		_ctx = null;
		_events = null;
		BumpVersion();
	}

	// ------------------------------------------------------------------ connection lifecycle
	void Component.INetworkListener.OnActive( Connection connection )
	{
		if ( !IsAuthority || IsHotseat )
			return;

		if ( Mode == GameMode.Lobby )
			AddSeatForConnection( connection );
	}

	void Component.INetworkListener.OnDisconnected( Connection connection )
	{
		if ( !IsAuthority || IsHotseat )
			return;

		// Basic handling: drop the seat in the lobby. Mid-game drops keep the player's
		// territories on the board; their turn is auto-skipped (eliminated = no territories).
		if ( Mode == GameMode.Lobby )
		{
			int idx = Seats.ToList().FindIndex( s => s.ConnectionId == connection.Id );
			if ( idx >= 0 )
			{
				Seats.RemoveAt( idx );
				BumpVersion();
			}
		}
	}

	private void AddSeatForConnection( Connection connection )
	{
		if ( connection == null || Data == null )
			return;

		if ( Seats.Any( s => s.ConnectionId == connection.Id ) )
			return;

		if ( Seats.Count >= MaxSeats )
			return;

		Seats.Add( new SeatInfo
		{
			PlayerId = "p_" + connection.Id.ToString( "N" ).Substring( 0, 8 ),
			FactionId = NextFreeFactionId(),
			DisplayName = connection.DisplayName ?? "Player",
			ConnectionId = connection.Id,
			IsReady = false,
			IsHuman = true,
		} );
		BumpVersion();
	}

	private string NextFreeFactionId()
	{
		var used = Seats.Select( s => s.FactionId ).ToHashSet();
		foreach ( var f in Data.Factions )
		{
			if ( !used.Contains( f.Id ) )
				return f.Id;
		}
		return Data.Factions[0].Id;
	}

	// ------------------------------------------------------------------ lobby RPCs
	[Rpc.Host]
	public void RequestSetFaction( string seatPlayerId, string factionId )
	{
		if ( !IsAuthority || Mode != GameMode.Lobby )
			return;

		int idx = IndexOfSeat( seatPlayerId );
		if ( idx < 0 )
			return;

		var seat = Seats[idx];
		if ( !CallerControls( seat ) )
			return;

		if ( Data.Faction( factionId ) == null )
			return;

		if ( Seats.Any( s => s.FactionId == factionId && s.PlayerId != seatPlayerId ) )
			return; // faction already taken

		seat.FactionId = factionId;
		Seats[idx] = seat;
		BumpVersion();
	}

	[Rpc.Host]
	public void RequestSetReady( string seatPlayerId, bool ready )
	{
		if ( !IsAuthority || Mode != GameMode.Lobby )
			return;

		int idx = IndexOfSeat( seatPlayerId );
		if ( idx < 0 )
			return;

		var seat = Seats[idx];
		if ( !CallerControls( seat ) )
			return;

		seat.IsReady = ready;
		Seats[idx] = seat;
		BumpVersion();
	}

	[Rpc.Host]
	public void RequestStart()
	{
		if ( !IsAuthority || Mode != GameMode.Lobby )
			return;

		if ( !CallerIsHost() )
			return;

		if ( Seats.Count < 2 )
			return;

		if ( Seats.Any( s => string.IsNullOrEmpty( s.FactionId ) ) )
			return;

		if ( !Seats.All( s => s.IsReady ) )
			return;

		HostStartGame();
	}

	// ------------------------------------------------------------------ gameplay (UI wrappers)
	public void Deploy( string territoryId, int count ) => RequestDeploy( CurrentPlayerId, territoryId, count );
	public void Attack( string fromId, string toId, int dice ) => RequestAttack( CurrentPlayerId, fromId, toId, dice );
	public void Fortify( string fromId, string toId, int count ) => RequestFortify( CurrentPlayerId, fromId, toId, count );
	public void EndPhase() { MiskAudio.Play( "phase" ); RequestEndPhase( CurrentPlayerId ); }
	public void TradeCards( string[] cardIds ) => RequestTradeCards( CurrentPlayerId, cardIds );
	public void Advance( int count ) => RequestAdvance( CurrentPlayerId, count );
	public void Claim( string territoryId ) { MiskAudio.Play( "deploy" ); RequestClaim( CurrentPlayerId, territoryId ); }
	public void Place( string territoryId ) { MiskAudio.Play( "deploy" ); RequestPlace( CurrentPlayerId, territoryId ); }

	[Rpc.Host]
	public void RequestDeploy( string playerId, string territoryId, int count )
	{
		if ( !IsAuthority || !CallerControlsPlayer( playerId ) )
			return;
		if ( RunCommand( new DeployCommand( playerId, territoryId, count ) ).Ok )
			BroadcastBoardEffect( (int)BoardEffectKind.Deploy, null, territoryId, count, 0, playerId, false );
	}

	[Rpc.Host]
	public void RequestAttack( string playerId, string fromId, string toId, int dice )
	{
		if ( !IsAuthority || !CallerControlsPlayer( playerId ) )
			return;
		RunCommand( new AttackCommand( playerId, fromId, toId, dice ) );
	}

	[Rpc.Host]
	public void RequestTradeCards( string playerId, string[] cardIds )
	{
		if ( !IsAuthority || !CallerControlsPlayer( playerId ) )
			return;
		RunCommand( new TradeCardsCommand( playerId, cardIds ) );
	}

	[Rpc.Host]
	public void RequestAdvance( string playerId, int count )
	{
		if ( !IsAuthority || !CallerControlsPlayer( playerId ) )
			return;
		var adv = _state?.PendingAdvance; // captured before the command clears it
		if ( RunCommand( new AdvanceArmiesCommand( playerId, count ) ).Ok && adv is { } a )
			BroadcastBoardEffect( (int)BoardEffectKind.Move, a.FromId, a.ToId, count, 0, playerId, false );
	}

	[Rpc.Host]
	public void RequestClaim( string playerId, string territoryId )
	{
		if ( !IsAuthority || !CallerControlsPlayer( playerId ) )
			return;
		RunSetupCommand( new ClaimCommand( playerId, territoryId ) );
	}

	[Rpc.Host]
	public void RequestPlace( string playerId, string territoryId )
	{
		if ( !IsAuthority || !CallerControlsPlayer( playerId ) )
			return;
		RunSetupCommand( new PlaceArmyCommand( playerId, territoryId ) );
	}

	[Rpc.Host]
	public void RequestFortify( string playerId, string fromId, string toId, int count )
	{
		if ( !IsAuthority || !CallerControlsPlayer( playerId ) )
			return;
		if ( RunCommand( new FortifyCommand( playerId, fromId, toId, count ) ).Ok )
			BroadcastBoardEffect( (int)BoardEffectKind.Move, fromId, toId, count, 0, playerId, false );
	}

	[Rpc.Host]
	public void RequestEndPhase( string playerId )
	{
		if ( !IsAuthority || !CallerControlsPlayer( playerId ) )
			return;
		if ( _controller == null )
			return;

		var result = _controller.EndPhase( playerId );
		if ( !result.Ok )
			Log.Info( $"[Misk] End phase rejected: {result.Error}" );
		BumpVersion();
	}

	/// <summary>Debug only: deal cards to the current player so the card tray can be inspected.</summary>
	public void DebugDealCards( int count )
	{
		if ( !IsAuthority || _ctx == null )
			return;
		for ( int i = 0; i < count; i++ )
			_ctx.DrawCard( CurrentPlayerId );
		BumpVersion();
	}

	private CommandResult RunCommand( IGameCommand command )
	{
		if ( _controller == null )
			return CommandResult.Fail( "No game in progress." );

		var result = _controller.Execute( command );
		if ( !result.Ok )
			Log.Info( $"[Misk] Command rejected: {result.Error}" );
		BumpVersion();
		return result;
	}

	private void RunSetupCommand( IGameCommand command )
	{
		if ( _setupController == null )
			return;

		var result = _setupController.Execute( command );
		if ( !result.Ok )
			Log.Info( $"[Misk] Setup action rejected: {result.Error}" );
		BumpVersion();
	}

	// ------------------------------------------------------------------ host simulation build
	private void HostStartGame()
	{
		GameData authoritative;
		try
		{
			authoritative = GameDataLoader.Load();
		}
		catch ( Exception e )
		{
			LoadError = e.Message;
			Log.Warning( $"[Misk] Could not start game: {e.Message}" );
			return;
		}

		var players = Seats
			.Select( s => new Player( s.PlayerId, s.FactionId, s.DisplayName, s.IsHuman ) )
			.ToList();

		_rng = new SystemDiceRoller();
		_events = new GameEventBus();
		_state = new GameState( authoritative.Map, authoritative.Rules, players );
		_ctx = new GameContext( _state, _rng, new DiceCombatResolver(),
			new StandardReinforcementCalculator(), _events );

		if ( authoritative.Rules.CardsEnabled )
			_state.Deck = new CardDeck( _state.Map, authoritative.Rules, _rng );

		// Reset the synced mirror.
		TurnOrder.Clear();
		foreach ( var p in players )
			TurnOrder.Add( p.Id );
		Armies.Clear();
		Owners.Clear();
		CardCounts.Clear();
		SetupRemaining.Clear();
		foreach ( var p in players )
			CardCounts[p.Id] = 0;
		CardSetsTradedIn = 0;
		ClearPendingAdvanceSync();
		CombatLog.Clear();
		WinnerPlayerId = null;
		Mode = GameMode.InGame;

		WireHostEvents();
		_controller = new TurnController( _ctx );

		if ( authoritative.Rules.ManualSetup )
		{
			// Board starts neutral; players draft it. SyncBoard mirrors empty owners/armies.
			SyncBoardFromState();
			_setupController = new SetupController( _ctx, _controller );
			_setupController.Begin();
		}
		else
		{
			GameSetup.AssignInitialState( _state, _rng );
			SyncBoardFromState();
			InSetup = false;
			_controller.StartGame();
		}

		StateVersion++;
	}

	/// <summary>Mirror every territory's owner/armies from authoritative state into the synced dictionaries.</summary>
	private void SyncBoardFromState()
	{
		foreach ( var t in _state.Map.Territories )
		{
			Armies[t.Id] = t.Armies;
			Owners[t.Id] = t.OwnerPlayerId;
		}
	}

	private void WireHostEvents()
	{
		_events.ArmiesChanged += e => Armies[e.TerritoryId] = e.Armies;
		_events.OwnerChanged += e => Owners[e.TerritoryId] = e.OwnerPlayerId;
		_events.PhaseChanged += e => PhaseIndex = (int)e.Phase;
		_events.ReinforcementsChanged += e => PendingReinforcements = e.Pending;
		_events.TurnChanged += e =>
		{
			CurrentPlayerIndex = _state.CurrentPlayerIndex;
			TurnNumber = _state.TurnNumber;
		};
		_events.GameWon += e =>
		{
			WinnerPlayerId = e.PlayerId;
			Mode = GameMode.GameOver;
		};
		_events.CombatResolved += e => BroadcastCombat(
			e.FromTerritoryId, e.ToTerritoryId, e.AttackerPlayerId, e.DefenderPlayerId,
			e.AttackerDice.ToArray(), e.DefenderDice.ToArray(),
			e.AttackerLosses, e.DefenderLosses, e.Captured );

		_events.HandChanged += e =>
		{
			CardCounts[e.PlayerId] = _state.Hand( e.PlayerId ).Count;
			DeliverHandTo( e.PlayerId );
		};
		_events.CardDrawn += e =>
		{
			var f = FactionOf( e.PlayerId );
			BroadcastLog( $"{f?.Name ?? "A host"} claims the spoils of war.", f?.Accent ?? "#e8dcc0", false );
		};
		_events.CardsTraded += e =>
		{
			var f = FactionOf( e.PlayerId );
			string bonus = e.TerritoryBonus > 0 ? $" (+{e.TerritoryBonus} on home soil)" : "";
			BroadcastLog( $"{f?.Name ?? "A host"} musters {e.Armies} levies from spoils{bonus}.", f?.Accent ?? "#c9a24a", true );
		};
		_events.AdvanceChanged += e => SyncAdvanceFromState();
		_events.SetupChanged += e => SyncSetupFromState();

		_events.Changed += () => StateVersion++;
	}

	// ------------------------------------------------------------------ sync writers
	private void SyncAdvanceFromState()
	{
		if ( _state?.PendingAdvance is { } advance )
		{
			PendingAdvanceFrom = advance.FromId;
			PendingAdvanceTo = advance.ToId;
			PendingAdvanceMin = advance.Min;
			PendingAdvanceMax = advance.Max;
		}
		else
		{
			ClearPendingAdvanceSync();
		}
	}

	private void ClearPendingAdvanceSync()
	{
		PendingAdvanceFrom = null;
		PendingAdvanceTo = null;
		PendingAdvanceMin = -1;
		PendingAdvanceMax = -1;
	}

	private void SyncSetupFromState()
	{
		InSetup = _state.InSetup;
		SetupStepIndex = (int)_state.SetupStep;
		CurrentPlayerIndex = _state.CurrentPlayerIndex; // the placer advances during setup
		SetupRemaining.Clear();
		foreach ( var kv in _state.SetupArmiesRemaining )
			SetupRemaining[kv.Key] = kv.Value;
	}

	// ------------------------------------------------------------------ private hand delivery
	private void DeliverHandTo( string playerId )
	{
		if ( !Networking.IsActive )
			return; // hotseat/offline: the authoritative state is read directly

		var seat = SeatOf( playerId );
		if ( seat == null )
			return;

		var conn = FindConnection( seat.Value.ConnectionId );
		if ( conn == null )
			return;

		string csv = HandCsv( playerId );
		using ( Rpc.FilterInclude( conn ) )
			DeliverHand( playerId, csv );
	}

	[Rpc.Broadcast]
	public void DeliverHand( string playerId, string csv )
	{
		_handCache[playerId] = ParseHand( csv );
	}

	private string HandCsv( string playerId )
		=> string.Join( ",", _state.Hand( playerId ).Select( c => $"{c.Id}|{(int)c.Kind}|{c.TerritoryId}" ) );

	private List<CardView> ParseHand( string csv )
	{
		var list = new List<CardView>();
		if ( string.IsNullOrEmpty( csv ) )
			return list;

		foreach ( var part in csv.Split( ',', StringSplitOptions.RemoveEmptyEntries ) )
		{
			var f = part.Split( '|' );
			if ( f.Length < 3 )
				continue;
			list.Add( new CardView
			{
				Id = f[0],
				Kind = (CardKind)int.Parse( f[1] ),
				TerritoryId = string.IsNullOrEmpty( f[2] ) ? null : f[2]
			} );
		}
		return list;
	}

	private static Connection FindConnection( Guid id )
	{
		foreach ( var c in Connection.All )
		{
			if ( c.Id == id )
				return c;
		}
		return null;
	}

	// ------------------------------------------------------------------ combat log broadcast
	[Rpc.Broadcast]
	public void BroadcastCombat( string fromId, string toId, string attackerId, string defenderId,
		int[] attackerDice, int[] defenderDice, int attackerLosses, int defenderLosses, bool captured )
	{
		var map = Data?.Map;
		string fromName = map?.Territory( fromId )?.Name ?? fromId;
		string toName = map?.Territory( toId )?.Name ?? toId;

		var attackerFaction = FactionOf( attackerId );
		string attackerName = attackerFaction?.Name ?? "Unknown host";
		string accent = attackerFaction?.Accent ?? "#e8dcc0";

		string aStr = attackerDice != null ? string.Join( " ", attackerDice ) : "";
		string dStr = defenderDice != null ? string.Join( " ", defenderDice ) : "";

		string message = captured
			? $"{attackerName} seizes {toName} from {fromName}!  [{aStr} | {dStr}]"
			: $"{attackerName} assaults {toName}  [{aStr} | {dStr}]  losses {attackerLosses}/{defenderLosses}";

		// Mark the battlefield for the blood/gore overlay on the map.
		LastCombatTerritoryId = toId;
		LastCombatCaptured = captured;

		// Drive the fight animation (lunge / clash / floating losses / dice / capture flash). This
		// broadcast already runs on every client, so each queues the effect locally.
		PushCombat( fromId, toId, attackerId, attackerDice, defenderDice, defenderLosses, attackerLosses, captured );
		MiskAudio.Play( "clash" );
		if ( captured )
			MiskAudio.Play( "horn" );

		CombatLog.Add( new CombatLogLine { Message = message, AccentHex = accent, Captured = captured } );
		while ( CombatLog.Count > 30 )
			CombatLog.RemoveAt( 0 );
	}

	/// <summary>Queue a march/deploy animation on every client (combat rides on BroadcastCombat instead).</summary>
	[Rpc.Broadcast]
	public void BroadcastBoardEffect( int kind, string fromId, string toId, int amount, int amount2, string playerId, bool captured )
	{
		var k = (BoardEffectKind)kind;
		PushEffect( k, fromId, toId, amount, amount2, playerId, captured );
		MiskAudio.Play( k == BoardEffectKind.Deploy ? "deploy" : "march" );
	}

	[Rpc.Broadcast]
	public void BroadcastLog( string message, string accent, bool emphasized )
	{
		CombatLog.Add( new CombatLogLine { Message = message, AccentHex = accent ?? "#e8dcc0", Captured = emphasized } );
		while ( CombatLog.Count > 30 )
			CombatLog.RemoveAt( 0 );
	}

	// ------------------------------------------------------------------ helpers
	private void BumpVersion() => StateVersion++;

	private int IndexOfSeat( string playerId )
	{
		for ( int i = 0; i < Seats.Count; i++ )
		{
			if ( Seats[i].PlayerId == playerId )
				return i;
		}
		return -1;
	}

	private bool CallerControls( SeatInfo seat )
	{
		if ( IsHotseat || !Networking.IsActive )
			return true;
		var caller = Rpc.Caller;
		return caller != null && seat.ConnectionId == caller.Id;
	}

	private bool CallerControlsPlayer( string playerId )
	{
		var seat = SeatOf( playerId );
		return seat.HasValue && CallerControls( seat.Value );
	}

	private bool CallerIsHost()
	{
		if ( IsHotseat || !Networking.IsActive )
			return true;
		var caller = Rpc.Caller;
		return caller == null || caller.IsHost;
	}
}
