using System;
using Misk.Domain;
using Misk.Data;

namespace Misk.Presentation;

/// <summary>
/// The networked heart of the game. Lives once in the scene on every client.
///
///  - The HOST holds the authoritative simulation (GameState/TurnController, see
///    MiskGame.Host.cs) and mutates the [Sync] mirror below.
///  - CLIENTS never simulate; they render purely from the synced fields plus their own
///    read-only copy of the static map/faction/theme data.
///
/// Fine-grained sync: NetDictionary for per-territory owner/armies, NetList for seats and
/// turn order, scalar [Sync] for turn/phase. The domain event bus drives every write.
///
/// This file holds the networked state, lifecycle and read accessors used by the UI;
/// host authority and RPC handlers live in MiskGame.Host.cs.
/// </summary>
public sealed partial class MiskGame : Component, Component.INetworkListener
{
	public static MiskGame Current { get; private set; }

	// ---------------------------------------------------------------- networked state
	[Sync] public GameMode Mode { get; set; } = GameMode.MainMenu;
	[Sync] public bool IsHotseat { get; set; }

	/// <summary>Bumped on every state change; folded into UI BuildHash so all clients redraw.</summary>
	[Sync] public int StateVersion { get; set; }

	[Sync] public NetList<SeatInfo> Seats { get; set; } = new();
	[Sync] public NetList<string> TurnOrder { get; set; } = new();

	[Sync] public NetDictionary<string, int> Armies { get; set; } = new();
	[Sync] public NetDictionary<string, string> Owners { get; set; } = new();

	[Sync] public int CurrentPlayerIndex { get; set; }
	[Sync] public int TurnNumber { get; set; } = 1;
	[Sync] public int PhaseIndex { get; set; }
	[Sync] public int PendingReinforcements { get; set; }
	[Sync] public string WinnerPlayerId { get; set; }

	// cards (public info: counts + escalation; hand contents are delivered privately)
	[Sync] public NetDictionary<string, int> CardCounts { get; set; } = new();
	[Sync] public int CardSetsTradedIn { get; set; }

	// pending capture advance (Min < 0 means none)
	[Sync] public string PendingAdvanceFrom { get; set; }
	[Sync] public string PendingAdvanceTo { get; set; }
	[Sync] public int PendingAdvanceMin { get; set; } = -1;
	[Sync] public int PendingAdvanceMax { get; set; } = -1;

	// manual draft setup
	[Sync] public bool InSetup { get; set; }
	[Sync] public int SetupStepIndex { get; set; }
	[Sync] public NetDictionary<string, int> SetupRemaining { get; set; } = new();

	// ---------------------------------------------------------------- per-client view data
	/// <summary>Read-only static data (map/factions/rules/theme) for rendering.</summary>
	public GameData Data { get; private set; }

	/// <summary>Set if the static data failed to load/validate; shown to the user.</summary>
	public string LoadError { get; private set; }

	/// <summary>Status text shown while seeking/joining an online lobby (local only).</summary>
	public string JoinStatus { get; set; }

	/// <summary>The territory of the most recent battle (for the blood/gore overlay). Local only.</summary>
	public string LastCombatTerritoryId { get; set; }
	public bool LastCombatCaptured { get; set; }

	/// <summary>Local, append-only combat log (filled by the OnCombat broadcast).</summary>
	public List<CombatLogLine> CombatLog { get; } = new();

	// ---- transient board animations (local; queued by host broadcasts, played by AnimationLayer) ----
	public enum BoardEffectKind { Move = 0, Combat = 1, Deploy = 2 }

	public struct BoardEffect
	{
		public BoardEffectKind Kind;
		public string FromId;
		public string ToId;
		public int Amount;     // moved count, or defender losses for combat
		public int Amount2;    // attacker losses (combat)
		public string PlayerId; // the acting player (for faction sprite / accent)
		public bool Captured;
		public int[] AttackerDice; // combat only — the rolled faces, for the dice animation
		public int[] DefenderDice;
		public float StartTime; // Time.Now when queued, per client
	}

	/// <summary>Time.Now of the most recent capture, drives the full-screen victory flash.</summary>
	public float LastFlashTime { get; private set; } = -999f;

	/// <summary>Short-lived visual effects (marches, fights, deploys). Not networked — replayed locally.</summary>
	public List<BoardEffect> ActiveEffects { get; } = new();

	/// <summary>Bumped whenever the effect list changes, so the animation layer rebuilds.</summary>
	public int EffectVersion { get; private set; }

	/// <summary>Queue a transient board effect on this client (called from the host broadcasts below).</summary>
	public void PushEffect( BoardEffectKind kind, string fromId, string toId, int amount, int amount2, string playerId, bool captured )
	{
		ActiveEffects.Add( new BoardEffect
		{
			Kind = kind,
			FromId = fromId,
			ToId = toId,
			Amount = amount,
			Amount2 = amount2,
			PlayerId = playerId,
			Captured = captured,
			StartTime = Time.Now,
		} );
		while ( ActiveEffects.Count > 24 )
			ActiveEffects.RemoveAt( 0 );
		EffectVersion++;
	}

	/// <summary>Queue a combat effect carrying the rolled dice (for the dice animation) and trigger
	/// the capture flash. Used by BroadcastCombat (runs on every client).</summary>
	public void PushCombat( string fromId, string toId, string attackerId,
		int[] attackerDice, int[] defenderDice, int defenderLosses, int attackerLosses, bool captured )
	{
		ActiveEffects.Add( new BoardEffect
		{
			Kind = BoardEffectKind.Combat,
			FromId = fromId,
			ToId = toId,
			Amount = defenderLosses,
			Amount2 = attackerLosses,
			PlayerId = attackerId,
			Captured = captured,
			AttackerDice = attackerDice,
			DefenderDice = defenderDice,
			StartTime = Time.Now,
		} );
		while ( ActiveEffects.Count > 24 )
			ActiveEffects.RemoveAt( 0 );
		EffectVersion++;
		if ( captured )
			LastFlashTime = Time.Now;
	}

	/// <summary>Drop effects older than maxAge seconds. Returns true if the list changed.</summary>
	public bool PruneEffects( float maxAge )
	{
		int before = ActiveEffects.Count;
		ActiveEffects.RemoveAll( e => Time.Now - e.StartTime > maxAge );
		if ( ActiveEffects.Count == before )
			return false;
		EffectVersion++;
		return true;
	}

	/// <summary>A card as the UI sees it (the local player's own hand only).</summary>
	public struct CardView
	{
		public string Id;
		public CardKind Kind;
		public string TerritoryId;
	}

	/// <summary>Privately-delivered hands keyed by player id (online clients). Host reads state directly.</summary>
	private readonly Dictionary<string, List<CardView>> _handCache = new();

	// ---------------------------------------------------------------- lifecycle
	protected override void OnEnabled()
	{
		Current = this;

		try
		{
			Data = StaticData.Current;
			LoadError = null;
		}
		catch ( Exception e )
		{
			LoadError = e.Message;
			Log.Warning( $"[Misk] Failed to load game data: {e.Message}" );
		}
	}

	protected override void OnDisabled()
	{
		if ( Current == this )
			Current = null;
	}

	// NOTE on the cursor: the whole game is a 2D UI on a ScreenPanel with no first-person
	// controller. Cursor visibility + clickability is driven by CSS `pointer-events: all` on the
	// UI root (see MiskRoot.razor.scss) — the s&box-recommended way. We deliberately do NOT set
	// Mouse.Visibility in code: forcing it (especially every frame) locks mouse input
	// (Facepunch sbox-issues #8365 — cursor shows but clicks never register).

	private bool _victoryPlayed;

	protected override void OnUpdate()
	{
		// Music bed runs through play and the victory screen; silenced in menu/lobby. EnsureMusic
		// loops it. Synced Mode means every client plays its own local audio.
		bool inGame = Mode == GameMode.InGame || Mode == GameMode.GameOver;
		if ( inGame )
			MiskAudio.EnsureMusic( "music" );
		else
		{
			MiskAudio.StopMusic();
			_victoryPlayed = false;
		}

		if ( Mode == GameMode.GameOver && WinnerPlayerId != null && !_victoryPlayed )
		{
			_victoryPlayed = true;
			MiskAudio.Play( "horn" );
		}
	}

	// ---------------------------------------------------------------- convenience accessors

	public GameMap Map => Data?.Map;
	public ThemeConfig Theme => Data?.Theme ?? ThemeConfig.Fallback();
	public GamePhase CurrentPhase => (GamePhase)PhaseIndex;

	/// <summary>True when this instance is allowed to run the simulation (host, hotseat or offline).</summary>
	public bool IsAuthority => IsHotseat || !Networking.IsActive || Networking.IsHost;

	/// <summary>True when this instance may press host-only controls (Start, etc.).</summary>
	public bool IsHost => !Networking.IsActive || Networking.IsHost;

	public int ArmiesOf( string territoryId ) => Armies.TryGetValue( territoryId, out var n ) ? n : 0;
	public string OwnerOf( string territoryId ) => Owners.TryGetValue( territoryId, out var id ) ? id : null;

	public string CurrentPlayerId =>
		(CurrentPlayerIndex >= 0 && CurrentPlayerIndex < TurnOrder.Count) ? TurnOrder[CurrentPlayerIndex] : null;

	public SeatInfo? SeatOf( string playerId )
	{
		foreach ( var seat in Seats )
		{
			if ( seat.PlayerId == playerId )
				return seat;
		}
		return null;
	}

	public Faction FactionOf( string playerId )
	{
		var seat = SeatOf( playerId );
		if ( seat == null || Data == null )
			return null;
		return Data.Faction( seat.Value.FactionId );
	}

	public Faction CurrentFaction => FactionOf( CurrentPlayerId );

	/// <summary>Colour to tint a territory by its owner (or a neutral tone if unowned).</summary>
	public string OwnerColor( string territoryId )
	{
		var owner = OwnerOf( territoryId );
		var faction = owner != null ? FactionOf( owner ) : null;
		return faction?.Color ?? "#5a5048";
	}

	public bool CanControlSeat( SeatInfo seat )
	{
		if ( IsHotseat || !Networking.IsActive )
			return true;
		return Connection.Local != null && seat.ConnectionId == Connection.Local.Id;
	}

	/// <summary>Whether the local machine is allowed to act for whoever's turn it currently is.</summary>
	public bool CanActNow
	{
		get
		{
			if ( Mode != GameMode.InGame || WinnerPlayerId != null )
				return false;
			var seat = SeatOf( CurrentPlayerId );
			return seat.HasValue && CanControlSeat( seat.Value );
		}
	}

	// ---------------------------------------------------------------- cards / advance / setup accessors

	public int CardCountOf( string playerId ) => CardCounts.TryGetValue( playerId, out var n ) ? n : 0;
	public int SetupRemainingOf( string playerId ) => SetupRemaining.TryGetValue( playerId, out var n ) ? n : 0;
	public SetupStep SetupStep => (SetupStep)SetupStepIndex;
	public bool HasPendingAdvance => PendingAdvanceMin >= 0 && !string.IsNullOrEmpty( PendingAdvanceTo );

	/// <summary>Army value the next traded set would be worth (for the card tray).</summary>
	public int NextSetValue => CardSetEvaluator.SetValue( CardSetsTradedIn, Data?.Rules ?? new RulesConfig() );

	/// <summary>The player id whose private hand the local machine should see.</summary>
	public string LocalPlayerId
	{
		get
		{
			if ( IsHotseat || !Networking.IsActive )
				return CurrentPlayerId; // local machine acts as whoever's turn it is
			var local = Connection.Local;
			if ( local == null )
				return CurrentPlayerId;
			foreach ( var seat in Seats )
			{
				if ( seat.ConnectionId == local.Id )
					return seat.PlayerId;
			}
			return null;
		}
	}

	/// <summary>The local player's own card hand (host/hotseat read authoritative state; clients read the private cache).</summary>
	public IReadOnlyList<CardView> MyHand
	{
		get
		{
			var pid = LocalPlayerId;
			if ( pid == null )
				return Array.Empty<CardView>();

			if ( IsAuthority && _state != null )
				return _state.Hand( pid ).Select( c => new CardView { Id = c.Id, Kind = c.Kind, TerritoryId = c.TerritoryId } ).ToList();

			return _handCache.TryGetValue( pid, out var list ) ? list : (IReadOnlyList<CardView>)Array.Empty<CardView>();
		}
	}

	/// <summary>True when the local player is forced to trade a set before they can end the reinforce phase.</summary>
	public bool MustTradeNow
	{
		get
		{
			if ( Data == null || !Data.Rules.CardsEnabled || CurrentPhase != GamePhase.Reinforce || !CanActNow )
				return false;
			var hand = MyHand;
			if ( hand.Count < Data.Rules.MandatoryTradeThreshold )
				return false;
			var cards = hand.Select( v => new Card( v.Id, v.Kind, v.TerritoryId ) ).ToList();
			return CardSetEvaluator.FindSet( cards ) != null;
		}
	}

	/// <summary>Single value the Razor panels hash on to decide when to rebuild.</summary>
	public int ViewHash
	{
		get
		{
			var hc = new HashCode();
			hc.Add( StateVersion );
			hc.Add( (int)Mode );
			hc.Add( PhaseIndex );
			hc.Add( CurrentPlayerIndex );
			hc.Add( PendingReinforcements );
			hc.Add( Seats.Count );
			hc.Add( CombatLog.Count );
			hc.Add( WinnerPlayerId );
			hc.Add( CardSetsTradedIn );
			hc.Add( PendingAdvanceMin );
			hc.Add( InSetup );
			hc.Add( SetupStepIndex );
			return hc.ToHashCode();
		}
	}
}
