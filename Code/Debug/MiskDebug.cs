using System.Collections.Generic;
using System.Linq;
using Misk.Domain;
using Misk.Presentation;

namespace Misk.Debugging;

/// <summary>
/// Debug/console driver for headless verification of the game loop (no mouse needed).
/// Drives the authoritative game through the same public wrappers the UI uses, so it
/// exercises the real path including manual setup, card trading and advance-after-capture.
/// Safe to delete.
///
///   misk_hotseat [seats]   start a hotseat match
///   misk_dump              log mode/turn/phase + per-player lands, armies and cards
///   misk_blitz [maxTurns]  auto-play (incl. setup) a trivial AI to completion
/// </summary>
public static class MiskDebug
{
	private static MiskGame Game => MiskGame.Current;

	[ConCmd( "misk_hotseat" )]
	public static void Hotseat( int seats = 4 )
	{
		var g = Game;
		if ( g is null ) { Log.Info( "[misk] no MiskGame in scene" ); return; }

		g.StartHotseatSetup( seats );
		g.RequestStart();
		Log.Info( $"[misk] started: mode={g.Mode} inSetup={g.InSetup} step={g.SetupStep} player={g.CurrentPlayerId}" );
	}

	[ConCmd( "misk_dump" )]
	public static void Dump()
	{
		var g = Game;
		if ( g is null ) return;

		Log.Info( $"[misk] mode={g.Mode} turn={g.TurnNumber} phase={g.CurrentPhase} inSetup={g.InSetup} cur={g.CurrentPlayerId} pending={g.PendingReinforcements} sets={g.CardSetsTradedIn} winner={g.WinnerPlayerId}" );
		foreach ( var p in g.TurnOrder )
		{
			int owned = g.Map.Territories.Count( t => g.OwnerOf( t.Id ) == p );
			int armies = g.Map.Territories.Where( t => g.OwnerOf( t.Id ) == p ).Sum( t => g.ArmiesOf( t.Id ) );
			Log.Info( $"   {g.FactionOf( p )?.Name ?? p}: {owned} lands, {armies} armies, {g.CardCountOf( p )} cards" );
		}
	}

	[ConCmd( "misk_host" )]
	public static void Host()
	{
		var g = Game;
		if ( g is null ) { Log.Info( "[misk] no MiskGame in scene" ); return; }
		g.StartOnlineHosting();
		Log.Info( $"[misk] hosting: active={Networking.IsActive} isHost={Networking.IsHost} mode={g.Mode} seats={g.Seats.Count}" );
	}

	[ConCmd( "misk_cardtest" )]
	public static void CardTest( int cards = 5 )
	{
		var g = Game;
		if ( g is null || g.Mode != GameMode.InGame ) { Log.Info( "[misk] start a game first: misk_hotseat" ); return; }
		AutoSetup( g );
		g.DebugDealCards( cards );
		Log.Info( $"[misk] dealt {cards} cards to {g.CurrentPlayerId}; phase={g.CurrentPhase}" );
	}

	[ConCmd( "misk_blitz" )]
	public static void Blitz( int maxTurns = 400 )
	{
		var g = Game;
		if ( g is null || g.Mode != GameMode.InGame ) { Log.Info( "[misk] start a game first: misk_hotseat" ); return; }

		AutoSetup( g );

		int turns = 0;
		while ( g.WinnerPlayerId is null && turns++ < maxTurns )
			PlayOneTurn( g );

		Log.Info( $"[misk] blitz finished after ~{turns} player-turns, {g.CardSetsTradedIn} card sets traded. Winner: {g.FactionOf( g.WinnerPlayerId )?.Name ?? "(none / hit cap)"}" );
	}

	// --- auto-complete the manual draft ---------------------------------------------------
	private static void AutoSetup( MiskGame g )
	{
		int guard = 0;
		while ( g.InSetup && guard++ < 6000 )
		{
			var me = g.CurrentPlayerId;
			if ( g.SetupStep == SetupStep.Claim )
			{
				var unclaimed = g.Map.Territories.FirstOrDefault( t => g.OwnerOf( t.Id ) == null );
				if ( unclaimed == null ) break;
				g.Claim( unclaimed.Id );
			}
			else
			{
				var mine = g.Map.Territories.FirstOrDefault( t => g.OwnerOf( t.Id ) == me );
				if ( mine == null ) break;
				g.Place( mine.Id );
			}
		}
	}

	// --- trivial greedy AI used only to drive the loop -----------------------------------
	private static void PlayOneTurn( MiskGame g )
	{
		var me = g.CurrentPlayerId;

		TradeSets( g );

		int safety = 0;
		while ( g.CurrentPhase == GamePhase.Reinforce && g.PendingReinforcements > 0 && safety++ < 500 )
		{
			var target = BestReinforceTarget( g, me );
			if ( target is null ) break;
			g.Deploy( target, g.PendingReinforcements );
		}
		if ( g.CurrentPhase == GamePhase.Reinforce ) g.EndPhase();

		safety = 0;
		while ( g.CurrentPhase == GamePhase.Attack && g.WinnerPlayerId is null && safety++ < 800 )
		{
			if ( g.HasPendingAdvance ) { g.Advance( g.PendingAdvanceMax ); continue; }

			var attack = BestAttack( g, me );
			if ( attack is null ) break;

			int dice = System.Math.Min( g.Data.Rules.AttackerMaxDice, System.Math.Max( 1, g.ArmiesOf( attack.Value.from ) - 1 ) );
			g.Attack( attack.Value.from, attack.Value.to, dice );
			if ( g.HasPendingAdvance ) g.Advance( g.PendingAdvanceMax );
		}
		if ( g.CurrentPhase == GamePhase.Attack && !g.HasPendingAdvance ) g.EndPhase();

		if ( g.CurrentPhase == GamePhase.Fortify ) g.EndPhase();
	}

	private static void TradeSets( MiskGame g )
	{
		int guard = 0;
		while ( guard++ < 20 )
		{
			var cards = g.MyHand.Select( v => new Card( v.Id, v.Kind, v.TerritoryId ) ).ToList();
			var set = CardSetEvaluator.FindSet( cards );
			if ( set == null ) break;
			g.TradeCards( set.Select( c => c.Id ).ToArray() );
		}
	}

	private static string BestReinforceTarget( MiskGame g, string me )
	{
		string best = null;
		int bestScore = -1;
		foreach ( var t in g.Map.Territories )
		{
			if ( g.OwnerOf( t.Id ) != me ) continue;
			bool border = t.AdjacentIds.Any( a => g.OwnerOf( a ) != me );
			int score = g.ArmiesOf( t.Id ) + (border ? 1000 : 0);
			if ( score > bestScore ) { bestScore = score; best = t.Id; }
		}
		return best;
	}

	private static (string from, string to)? BestAttack( MiskGame g, string me )
	{
		(string from, string to)? best = null;
		int bestEdge = 0;
		foreach ( var t in g.Map.Territories )
		{
			if ( g.OwnerOf( t.Id ) != me || g.ArmiesOf( t.Id ) < 2 ) continue;
			foreach ( var adjId in t.AdjacentIds )
			{
				if ( g.OwnerOf( adjId ) == me ) continue;
				int edge = g.ArmiesOf( t.Id ) - g.ArmiesOf( adjId );
				if ( edge > 0 && edge > bestEdge )
				{
					bestEdge = edge;
					best = (t.Id, adjId);
				}
			}
		}
		return best;
	}
}
