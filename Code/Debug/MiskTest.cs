using System;
using System.Collections.Generic;
using System.Linq;
using Misk.Domain;
using Misk.Data;

namespace Misk.Debugging;

/// <summary>
/// Deterministic verification battery for the engine-agnostic domain. Each check builds an
/// isolated GameState (with scripted dice so combat outcomes are exact) and asserts a single
/// mechanic. Results render in TestReport.razor (run with the `misk_test` console command).
/// This is the real "every mechanic" proof; networking/UI integration is verified separately
/// by the in-game blitz.
/// </summary>
public static class MiskTest
{
	public struct Result { public string Name; public bool Pass; public string Detail; }

	public static readonly List<Result> Results = new();
	public static bool Show;

	public static int Passed => Results.Count( r => r.Pass );
	public static int Total => Results.Count;

	[ConCmd( "misk_test" )]
	public static void Run()
	{
		Results.Clear();
		Combat();
		AttackAndCapture();
		Reinforcement();
		Fortify();
		Cards();
		Setup();
		TurnLoop();
		Geometry();
		DataAndValidation();
		Show = true;
		Log.Info( $"[misk] tests: {Passed}/{Total} passed" );
	}

	[ConCmd( "misk_test_hide" )]
	public static void Hide() => Show = false;

	// ----------------------------------------------------------------- harness
	private static void T( string name, Func<(bool ok, string detail)> body )
	{
		try
		{
			var r = body();
			Results.Add( new Result { Name = name, Pass = r.ok, Detail = r.detail } );
		}
		catch ( Exception e )
		{
			Results.Add( new Result { Name = name, Pass = false, Detail = "EXC: " + e.Message } );
		}
	}

	private sealed class ScriptedDice : IDiceRoller
	{
		private readonly Queue<int> _rolls;
		public ScriptedDice( params int[] rolls ) => _rolls = new Queue<int>( rolls );
		public int Roll() => _rolls.Count > 0 ? _rolls.Dequeue() : 1;
		public int Next( int maxExclusive ) => 0; // deterministic for shuffles/placement
	}

	private static (GameState st, GameContext ctx, TurnController tc) Build( int players, IDiceRoller rng )
	{
		var data = GameDataLoader.Load();
		var rules = new RulesConfig();
		var list = new List<Player>();
		for ( int i = 0; i < players; i++ )
			list.Add( new Player( $"p{i}", data.Factions[i % data.Factions.Count].Id, $"P{i}", true ) );

		var bus = new GameEventBus();
		var st = new GameState( data.Map, rules, list );
		var ctx = new GameContext( st, rng, new DiceCombatResolver(), new StandardReinforcementCalculator(), bus );
		var tc = new TurnController( ctx );
		return (st, ctx, tc);
	}

	private static void Neutralize( GameState st, string owner )
	{
		foreach ( var t in st.Map.Territories ) { t.OwnerPlayerId = owner; t.Armies = 1; }
	}

	private static void Give( GameState st, string owner, params string[] ids )
	{
		foreach ( var id in ids ) { var t = st.Map.Territory( id ); t.OwnerPlayerId = owner; t.Armies = 1; }
	}

	private static string Yes( bool b ) => b ? "ok" : "FAIL";

	// ----------------------------------------------------------------- combat resolver
	private static void Combat()
	{
		var rules = new RulesConfig();

		T( "combat: attacker sweeps (6,6,6 vs 1,1) → defender loses 2", () =>
		{
			var o = new DiceCombatResolver().Resolve( 4, 2, 3, rules, new ScriptedDice( 6, 6, 6, 1, 1 ) );
			return (o.DefenderLosses == 2 && o.AttackerLosses == 0, $"A{o.AttackerLosses}/D{o.DefenderLosses}");
		} );

		T( "combat: ties go to defender (5,5 vs 5,5) → attacker loses 2", () =>
		{
			var o = new DiceCombatResolver().Resolve( 3, 2, 2, rules, new ScriptedDice( 5, 5, 5, 5 ) );
			return (o.AttackerLosses == 2 && o.DefenderLosses == 0, $"A{o.AttackerLosses}/D{o.DefenderLosses}");
		} );

		T( "combat: mixed (6,2 vs 5,3) → 1 loss each", () =>
		{
			var o = new DiceCombatResolver().Resolve( 3, 2, 2, rules, new ScriptedDice( 6, 2, 5, 3 ) );
			return (o.AttackerLosses == 1 && o.DefenderLosses == 1, $"A{o.AttackerLosses}/D{o.DefenderLosses}");
		} );

		T( "combat: attacker dice clamped to armies-1", () =>
		{
			var o = new DiceCombatResolver().Resolve( 2, 1, 3, rules, new ScriptedDice( 6, 1 ) );
			return (o.AttackerDice.Count == 1, $"rolled {o.AttackerDice.Count}");
		} );
	}

	// ----------------------------------------------------------------- attack / capture / advance
	private static void AttackAndCapture()
	{
		T( "attack: losses applied to both territories", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice( 1, 5 ) ); // att low, def high → att loses 1
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere" );
			st.Map.Territory( "frostmere" ).Armies = 3;
			st.Map.Territory( "icereach" ).Armies = 2;
			st.CurrentPlayerIndex = 0;
			new AttackCommand( "p0", "frostmere", "icereach", 1 ).Execute( ctx );
			return (st.Map.Territory( "frostmere" ).Armies == 2, $"from={st.Map.Territory( "frostmere" ).Armies}");
		} );

		T( "capture: ownership transfers + PendingAdvance offered", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice( 6, 6, 6, 1 ) );
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere" );
			st.Map.Territory( "frostmere" ).Armies = 5;
			st.Map.Territory( "icereach" ).Armies = 1;
			st.CurrentPlayerIndex = 0;
			new AttackCommand( "p0", "frostmere", "icereach", 3 ).Execute( ctx );
			bool owned = st.Map.Territory( "icereach" ).OwnerPlayerId == "p0";
			bool pend = st.PendingAdvance is { Min: 3, Max: 4 };
			return (owned && pend, $"owned={Yes( owned )} pend={Yes( pend )}");
		} );

		T( "advance: resolves pending and moves armies", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice( 6, 6, 6, 1 ) );
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere" );
			st.Map.Territory( "frostmere" ).Armies = 5;
			st.Map.Territory( "icereach" ).Armies = 1;
			st.CurrentPlayerIndex = 0;
			new AttackCommand( "p0", "frostmere", "icereach", 3 ).Execute( ctx );
			new AdvanceArmiesCommand( "p0", 4 ).Execute( ctx );
			bool moved = st.Map.Territory( "icereach" ).Armies == 4 && st.Map.Territory( "frostmere" ).Armies == 1;
			return (moved && st.PendingAdvance == null, $"to={st.Map.Territory( "icereach" ).Armies} from={st.Map.Territory( "frostmere" ).Armies}");
		} );

		T( "capture: auto-advances when only one amount is legal", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice( 6, 1 ) );
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere" );
			st.Map.Territory( "frostmere" ).Armies = 2;
			st.Map.Territory( "icereach" ).Armies = 1;
			st.CurrentPlayerIndex = 0;
			new AttackCommand( "p0", "frostmere", "icereach", 1 ).Execute( ctx );
			bool auto = st.PendingAdvance == null && st.Map.Territory( "icereach" ).Armies == 1 && st.Map.Territory( "frostmere" ).Armies == 1;
			return (auto, $"to={st.Map.Territory( "icereach" ).Armies}");
		} );

		T( "attack rejected: not adjacent", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice() );
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere" );
			st.Map.Territory( "frostmere" ).Armies = 5; st.CurrentPlayerIndex = 0;
			var r = new AttackCommand( "p0", "frostmere", "graven_pass", 1 ).Validate( ctx );
			return (!r.Ok, r.Error ?? "ok");
		} );

		T( "attack rejected: too few armies", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice() );
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere" );
			st.Map.Territory( "frostmere" ).Armies = 1; st.CurrentPlayerIndex = 0;
			var r = new AttackCommand( "p0", "frostmere", "icereach", 1 ).Validate( ctx );
			return (!r.Ok, r.Error ?? "ok");
		} );

		T( "attack rejected: not your turn", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice() );
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere" );
			st.Map.Territory( "frostmere" ).Armies = 5; st.CurrentPlayerIndex = 1; // p1's turn
			var r = new AttackCommand( "p0", "frostmere", "icereach", 1 ).Validate( ctx );
			return (!r.Ok, r.Error ?? "ok");
		} );

		T( "guard: actions blocked while a capture is pending", () =>
		{
			var (st, _, tc) = Build( 2, new ScriptedDice() );
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere", "icereach" );
			st.CurrentPlayerIndex = 0;
			st.PendingAdvance = new AdvanceRequest { FromId = "frostmere", ToId = "icereach", Min = 1, Max = 2 };
			var r = tc.Execute( new FortifyCommand( "p0", "frostmere", "icereach", 1 ) );
			return (!r.Ok, r.Error ?? "ok");
		} );
	}

	// ----------------------------------------------------------------- reinforcement / deploy
	private static void Reinforcement()
	{
		T( "reinforce: base = max(3, floor(territories/3))", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice() );
			Neutralize( st, "p1" );
			Give( st, "p0", "frostmere", "mistveil", "thornhall", "gloomroot" ); // 4 mixed regions
			int n = new StandardReinforcementCalculator().Calculate( st, "p0" );
			return (n == 3, $"got {n}");
		} );

		T( "reinforce: full region grants its bonus", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice() );
			Neutralize( st, "p1" );
			Give( st, "p0", "frostmere", "icereach", "graven_pass", "wolfsthrone" ); // all of Northern Holds (bonus 3)
			int n = new StandardReinforcementCalculator().Calculate( st, "p0" );
			return (n == 6, $"got {n} (expect 3 base + 3 region)");
		} );

		T( "reinforce: partial region grants no bonus", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice() );
			Neutralize( st, "p1" );
			Give( st, "p0", "frostmere", "icereach", "graven_pass" ); // 3 of 4
			int n = new StandardReinforcementCalculator().Calculate( st, "p0" );
			return (n == 3, $"got {n}");
		} );

		T( "deploy: adds armies and reduces pending", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice() );
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere" );
			st.CurrentPlayerIndex = 0; st.PendingReinforcements = 5;
			new DeployCommand( "p0", "frostmere", 3 ).Execute( ctx );
			return (st.Map.Territory( "frostmere" ).Armies == 4 && st.PendingReinforcements == 2, $"armies={st.Map.Territory( "frostmere" ).Armies} pending={st.PendingReinforcements}");
		} );

		T( "deploy rejected: more than pending / not owned", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice() );
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere" );
			st.CurrentPlayerIndex = 0; st.PendingReinforcements = 2;
			bool tooMany = !new DeployCommand( "p0", "frostmere", 5 ).Validate( ctx ).Ok;
			bool notOwned = !new DeployCommand( "p0", "icereach", 1 ).Validate( ctx ).Ok;
			return (tooMany && notOwned, $"tooMany={Yes( tooMany )} notOwned={Yes( notOwned )}");
		} );
	}

	// ----------------------------------------------------------------- fortify
	private static void Fortify()
	{
		T( "fortify: moves between adjacent friendly, leaves min", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice() );
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere", "icereach" );
			st.Map.Territory( "frostmere" ).Armies = 5; st.CurrentPlayerIndex = 0;
			new FortifyCommand( "p0", "frostmere", "icereach", 3 ).Execute( ctx );
			return (st.Map.Territory( "frostmere" ).Armies == 2 && st.Map.Territory( "icereach" ).Armies == 4, $"from={st.Map.Territory( "frostmere" ).Armies} to={st.Map.Territory( "icereach" ).Armies}");
		} );

		T( "fortify rejected: would leave < min / not adjacent", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice() );
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere", "icereach", "graven_pass" );
			st.Map.Territory( "frostmere" ).Armies = 3; st.CurrentPlayerIndex = 0;
			bool leaveTooFew = !new FortifyCommand( "p0", "frostmere", "icereach", 3 ).Validate( ctx ).Ok;
			bool notAdj = !new FortifyCommand( "p0", "frostmere", "graven_pass", 1 ).Validate( ctx ).Ok;
			return (leaveTooFew && notAdj, $"leave={Yes( leaveTooFew )} adj={Yes( notAdj )}");
		} );

		T( "fortify: limited to moves-per-turn", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice() );
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere", "icereach" );
			st.Map.Territory( "frostmere" ).Armies = 9; st.CurrentPlayerIndex = 0;
			new FortifyCommand( "p0", "frostmere", "icereach", 1 ).Execute( ctx );
			st.FortifyMovesUsed = 1; // as the phase would set
			var r = new FortifyCommand( "p0", "frostmere", "icereach", 1 ).Validate( ctx );
			return (!r.Ok, r.Error ?? "ok");
		} );
	}

	// ----------------------------------------------------------------- cards
	private static void Cards()
	{
		T( "cards: deck = territories + wilds", () =>
		{
			var (st, _, _) = Build( 2, new ScriptedDice() );
			var deck = new CardDeck( st.Map, st.Rules, new ScriptedDice() );
			return (deck.Count == st.Map.Territories.Count + st.Rules.WildCardCount, $"count={deck.Count}");
		} );

		T( "cards: set validity (same / one-each / wild / invalid)", () =>
		{
			var f1 = new Card( "a", CardKind.Footmen, "x" );
			var f2 = new Card( "b", CardKind.Footmen, "y" );
			var f3 = new Card( "c", CardKind.Footmen, "z" );
			var r1 = new Card( "d", CardKind.Riders, "y" );
			var s1 = new Card( "e", CardKind.Siege, "z" );
			var w = new Card( "w", CardKind.Banner, null );
			bool same = CardSetEvaluator.IsValidSet( f1, f2, f3 );
			bool oneEach = CardSetEvaluator.IsValidSet( f1, r1, s1 );
			bool wild = CardSetEvaluator.IsValidSet( f1, f2, w );
			bool invalid = !CardSetEvaluator.IsValidSet( f1, f2, r1 );
			return (same && oneEach && wild && invalid, $"same={Yes( same )} each={Yes( oneEach )} wild={Yes( wild )} inv={Yes( invalid )}");
		} );

		T( "cards: escalating set value (4,6,…,15,+5)", () =>
		{
			var r = new RulesConfig();
			bool ok = CardSetEvaluator.SetValue( 0, r ) == 4 && CardSetEvaluator.SetValue( 5, r ) == 15 && CardSetEvaluator.SetValue( 6, r ) == 20;
			return (ok, $"{CardSetEvaluator.SetValue( 0, r )},{CardSetEvaluator.SetValue( 5, r )},{CardSetEvaluator.SetValue( 6, r )}");
		} );

		T( "cards: trade awards value, removes cards, bumps counter", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice() );
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere" );
			st.CurrentPlayerIndex = 0; st.PendingReinforcements = 0;
			st.Hand( "p0" ).AddRange( new[]
			{
				new Card( "c1", CardKind.Footmen, "icereach" ),
				new Card( "c2", CardKind.Riders, "graven_pass" ),
				new Card( "c3", CardKind.Siege, "wolfsthrone" ),
			} );
			new TradeCardsCommand( "p0", new[] { "c1", "c2", "c3" } ).Execute( ctx );
			bool ok = st.PendingReinforcements == 4 && st.CardSetsTradedIn == 1 && st.Hand( "p0" ).Count == 0;
			return (ok, $"pending={st.PendingReinforcements} sets={st.CardSetsTradedIn} hand={st.Hand( "p0" ).Count}");
		} );

		T( "cards: +2 bonus on an owned depicted territory", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice() );
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere" );
			st.CurrentPlayerIndex = 0; st.PendingReinforcements = 0;
			st.Map.Territory( "frostmere" ).Armies = 1;
			st.Hand( "p0" ).AddRange( new[]
			{
				new Card( "c1", CardKind.Footmen, "frostmere" ), // owned
				new Card( "c2", CardKind.Riders, "graven_pass" ),
				new Card( "c3", CardKind.Siege, "wolfsthrone" ),
			} );
			new TradeCardsCommand( "p0", new[] { "c1", "c2", "c3" } ).Execute( ctx );
			return (st.Map.Territory( "frostmere" ).Armies == 3, $"frostmere armies={st.Map.Territory( "frostmere" ).Armies}");
		} );

		T( "cards: eliminating a player steals their hand", () =>
		{
			var (st, ctx, _) = Build( 3, new ScriptedDice( 6, 1 ) );
			Neutralize( st, "p2" );
			Give( st, "p0", "frostmere" );
			Give( st, "p1", "icereach" ); // p1's only land
			st.Map.Territory( "frostmere" ).Armies = 2;
			st.Map.Territory( "icereach" ).Armies = 1;
			st.Hand( "p1" ).AddRange( new[] { new Card( "x", CardKind.Footmen, "a" ), new Card( "y", CardKind.Riders, "b" ) } );
			st.CurrentPlayerIndex = 0;
			new AttackCommand( "p0", "frostmere", "icereach", 1 ).Execute( ctx );
			return (st.Hand( "p0" ).Count == 2 && st.Hand( "p1" ).Count == 0, $"p0={st.Hand( "p0" ).Count} p1={st.Hand( "p1" ).Count}");
		} );

		T( "cards: mandatory trade triggers at the hand limit", () =>
		{
			var (st, ctx, _) = Build( 2, new ScriptedDice() );
			st.CurrentPlayerIndex = 0;
			for ( int i = 0; i < 4; i++ ) st.Hand( "p0" ).Add( new Card( $"c{i}", (CardKind)(i % 3), "x" ) );
			bool four = !ReinforcePhaseState.MustTrade( ctx );
			st.Hand( "p0" ).Add( new Card( "c5", CardKind.Footmen, "x" ) ); // 5 → has a set
			bool five = ReinforcePhaseState.MustTrade( ctx );
			return (four && five, $"at4={Yes( four )} at5={Yes( five )}");
		} );
	}

	// ----------------------------------------------------------------- manual setup
	private static void Setup()
	{
		T( "setup: Begin enters Claim with seeded pools", () =>
		{
			var (st, ctx, tc) = Build( 2, new ScriptedDice() );
			var setup = new SetupController( ctx, tc );
			setup.Begin();
			bool ok = st.InSetup && st.SetupStep == SetupStep.Claim && st.SetupArmiesRemaining["p0"] == st.Rules.StartingArmiesFor( 2 );
			return (ok, $"inSetup={Yes( st.InSetup )} pool={st.SetupArmiesRemaining["p0"]}");
		} );

		T( "setup: claim assigns owner, spends an army, passes turn", () =>
		{
			var (st, ctx, tc) = Build( 2, new ScriptedDice() );
			var setup = new SetupController( ctx, tc );
			setup.Begin();
			int pool = st.SetupArmiesRemaining["p0"];
			setup.Execute( new ClaimCommand( "p0", "frostmere" ) );
			bool ok = st.Map.Territory( "frostmere" ).OwnerPlayerId == "p0"
				&& st.SetupArmiesRemaining["p0"] == pool - 1
				&& st.CurrentPlayerIndex == 1;
			return (ok, $"owner={st.Map.Territory( "frostmere" ).OwnerPlayerId} placer={st.CurrentPlayerIndex}");
		} );

		T( "setup: drives to completion → game starts at turn 1 reinforce", () =>
		{
			var (st, ctx, tc) = Build( 2, new ScriptedDice() );
			var setup = new SetupController( ctx, tc );
			setup.Begin();
			int guard = 0;
			bool sawPlace = false;
			while ( st.InSetup && guard++ < 5000 )
			{
				if ( st.SetupStep == SetupStep.Place ) sawPlace = true;
				var me = st.CurrentPlayer.Id;
				if ( st.SetupStep == SetupStep.Claim )
				{
					var unclaimed = st.Map.Territories.FirstOrDefault( t => t.OwnerPlayerId == null );
					setup.Execute( new ClaimCommand( me, unclaimed.Id ) );
				}
				else
				{
					var mine = st.Map.Territories.First( t => t.OwnerPlayerId == me );
					setup.Execute( new PlaceArmyCommand( me, mine.Id ) );
				}
			}
			bool ok = !st.InSetup && sawPlace && st.Phase == GamePhase.Reinforce && st.TurnNumber == 1;
			return (ok, $"inSetup={st.InSetup} sawPlace={Yes( sawPlace )} phase={st.Phase}");
		} );
	}

	// ----------------------------------------------------------------- turn loop / victory
	private static void TurnLoop()
	{
		T( "loop: cannot leave reinforce with armies undeployed", () =>
		{
			var (st, ctx, tc) = Build( 2, new ScriptedDice() );
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere" );
			tc.StartGame();
			var r = tc.EndPhase( "p0" );
			return (!r.Ok && st.Phase == GamePhase.Reinforce, $"phase={st.Phase}");
		} );

		T( "loop: reinforce → attack → fortify → next player", () =>
		{
			var (st, ctx, tc) = Build( 2, new ScriptedDice() );
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere" );
			tc.StartGame();
			while ( st.PendingReinforcements > 0 ) tc.Execute( new DeployCommand( "p0", "frostmere", st.PendingReinforcements ) );
			tc.EndPhase( "p0" ); var atAttack = st.Phase == GamePhase.Attack;
			tc.EndPhase( "p0" ); var atFortify = st.Phase == GamePhase.Fortify;
			tc.EndPhase( "p0" ); var nextPlayer = st.CurrentPlayer.Id == "p1" && st.Phase == GamePhase.Reinforce;
			return (atAttack && atFortify && nextPlayer, $"atk={Yes( atAttack )} fort={Yes( atFortify )} next={st.CurrentPlayer.Id}");
		} );

		T( "loop: eliminated players are skipped", () =>
		{
			var (st, ctx, tc) = Build( 3, new ScriptedDice() );
			Neutralize( st, "p0" ); Give( st, "p2", "vael_drath" ); // p1 owns nothing
			tc.StartGame();
			while ( st.PendingReinforcements > 0 ) tc.Execute( new DeployCommand( "p0", FirstOwned( st, "p0" ), st.PendingReinforcements ) );
			tc.EndPhase( "p0" ); tc.EndPhase( "p0" ); tc.EndPhase( "p0" ); // end p0's turn
			return (st.CurrentPlayer.Id == "p2", $"next={st.CurrentPlayer.Id} (p1 has no land)");
		} );

		T( "victory: one owner of every territory wins", () =>
		{
			var (st, _, _) = Build( 2, new ScriptedDice() );
			Neutralize( st, "p0" );
			return (VictoryChecker.CheckWinner( st ) == "p0", VictoryChecker.CheckWinner( st ) ?? "null");
		} );

		T( "earn: capturing flags a card draw at end of turn", () =>
		{
			var (st, ctx, tc) = Build( 2, new ScriptedDice( 6, 1 ) );
			Neutralize( st, "p1" ); Give( st, "p0", "frostmere" );
			st.Deck = new CardDeck( st.Map, st.Rules, new ScriptedDice() );
			st.Map.Territory( "frostmere" ).Armies = 2;
			st.Map.Territory( "icereach" ).Armies = 1;
			tc.StartGame();
			while ( st.PendingReinforcements > 0 ) tc.Execute( new DeployCommand( "p0", "frostmere", st.PendingReinforcements ) );
			tc.EndPhase( "p0" ); // → attack
			tc.Execute( new AttackCommand( "p0", "frostmere", "icereach", 1 ) ); // capture
			if ( st.PendingAdvance is { } adv ) tc.Execute( new AdvanceArmiesCommand( "p0", adv.Min ) ); // resolve advance
			int before = st.Hand( "p0" ).Count;
			tc.EndPhase( "p0" ); // → fortify
			tc.EndPhase( "p0" ); // end turn → draw
			return (st.Hand( "p0" ).Count == before + 1, $"hand {before}→{st.Hand( "p0" ).Count}");
		} );
	}

	private static string FirstOwned( GameState st, string p ) => st.Map.Territories.First( t => t.OwnerPlayerId == p ).Id;

	// ----------------------------------------------------------------- geometry (region cells)
	private static void Geometry()
	{
		// A simple square polygon, used directly so the test doesn't depend on map authoring.
		var square = new List<MapPoint> { new( 0, 0 ), new( 100, 0 ), new( 100, 100 ), new( 0, 100 ) };
		var sq = new Territory( "sq", "Square", "r", new List<string>(), new MapPoint( 50, 50 ), square );

		T( "geo: point inside polygon is contained", () =>
			(sq.ContainsPoint( new MapPoint( 50, 50 ) ), "centre") );

		T( "geo: point outside polygon is not contained", () =>
			(!sq.ContainsPoint( new MapPoint( 150, 50 ) ), "x=150") );

		T( "geo: point just outside an edge is not contained", () =>
			(!sq.ContainsPoint( new MapPoint( 100.5f, 50 ) ), "x=100.5") );

		T( "geo: territory with no shape contains nothing", () =>
		{
			var pt = new Territory( "p", "Point", "r", new List<string>(), new MapPoint( 10, 10 ) );
			return (!pt.HasShape && !pt.ContainsPoint( new MapPoint( 10, 10 ) ), $"hasShape={pt.HasShape}");
		} );

		T( "geo: every authored territory contains its own badge position", () =>
		{
			var map = GameDataLoader.Load().Map;
			var bad = map.Territories.Where( t => t.HasShape && !t.ContainsPoint( t.Position ) ).Select( t => t.Id ).ToList();
			return (bad.Count == 0, bad.Count == 0 ? "all 20 ok" : "outside: " + string.Join( ",", bad ));
		} );

		T( "geo: authored region cells do not overlap at their badge points", () =>
		{
			// Each territory's badge point should fall in exactly one cell (no double-coverage).
			var map = GameDataLoader.Load().Map;
			var shaped = map.Territories.Where( t => t.HasShape ).ToList();
			var clashes = new List<string>();
			foreach ( var t in shaped )
			{
				int hits = shaped.Count( o => o.ContainsPoint( t.Position ) );
				if ( hits != 1 ) clashes.Add( $"{t.Id}:{hits}" );
			}
			return (clashes.Count == 0, clashes.Count == 0 ? $"{shaped.Count} clean" : string.Join( " ", clashes ));
		} );
	}

	// ----------------------------------------------------------------- data
	private static void DataAndValidation()
	{
		T( "data: real map loads & validates (20/5/6)", () =>
		{
			var d = GameDataLoader.Load();
			bool ok = d.Map.Territories.Count == 20 && d.Map.Regions.Count == 5 && d.Factions.Count == 6;
			return (ok, $"terr={d.Map.Territories.Count} reg={d.Map.Regions.Count} fac={d.Factions.Count} warns={d.Warnings.Count}");
		} );

		T( "data: adjacency is symmetric", () =>
		{
			var d = GameDataLoader.Load();
			foreach ( var t in d.Map.Territories )
				foreach ( var a in t.AdjacentIds )
					if ( !(d.Map.Territory( a )?.IsAdjacentTo( t.Id ) ?? false) )
						return (false, $"{t.Id}→{a} not mutual");
			return (true, "all mutual");
		} );
	}
}
