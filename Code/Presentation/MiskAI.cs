using System;
using System.Linq;
using Misk.Domain;

namespace Misk.Presentation;

/// <summary>
/// The computer commander. Drives a non-human seat one atomic action at a time through the same
/// public wrappers the UI uses (Claim/Place/Deploy/Attack/Advance/Fortify/EndPhase) — so AI moves
/// get the same animations, sounds and host-authoritative validation as a human's. The host calls
/// <see cref="Step"/> on a throttle (see MiskGame.OnUpdate) so its turn is watchable.
///
/// Strategy is intentionally a solid "Normal": concentrate reinforcements on the strongest border,
/// press favourable attacks, cash in card sets, then end. Difficulty tiers can branch later.
/// </summary>
public static class MiskAI
{
	/// <summary>Perform a single action for whoever's (non-human) turn it currently is, then return.</summary>
	public static void Step( MiskGame g )
	{
		if ( g == null || g.Map == null || g.Data == null || g.CurrentPlayerId == null )
			return;

		if ( g.InSetup )
		{
			StepSetup( g );
			return;
		}

		// A capture left armies to pour forward — resolve it before anything else.
		if ( g.HasPendingAdvance )
		{
			g.Advance( g.PendingAdvanceMax );
			return;
		}

		switch ( g.CurrentPhase )
		{
			case GamePhase.Reinforce:
				StepReinforce( g );
				break;
			case GamePhase.Attack:
				StepAttack( g );
				break;
			default: // Fortify — keep it simple: hold position and end the turn.
				g.EndPhase();
				break;
		}
	}

	// ----------------------------------------------------------------- setup draft
	private static void StepSetup( MiskGame g )
	{
		var me = g.CurrentPlayerId;
		if ( g.SetupStep == SetupStep.Claim )
		{
			// Prefer an unclaimed land next to one we already hold (builds a contiguous front).
			var adjacent = g.Map.Territories.FirstOrDefault( t =>
				g.OwnerOf( t.Id ) == null && t.AdjacentIds.Any( a => g.OwnerOf( a ) == me ) );
			var pick = adjacent ?? g.Map.Territories.FirstOrDefault( t => g.OwnerOf( t.Id ) == null );
			if ( pick != null )
				g.Claim( pick.Id );
		}
		else
		{
			// Muster onto a border territory so the starting army does work.
			var target = BestReinforceTarget( g, me ) ?? g.Map.Territories.FirstOrDefault( t => g.OwnerOf( t.Id ) == me )?.Id;
			if ( target != null )
				g.Place( target );
		}
	}

	// ----------------------------------------------------------------- reinforce
	private static void StepReinforce( MiskGame g )
	{
		var set = FindSet( g );
		if ( set != null )
		{
			g.TradeCards( set );
			return;
		}

		if ( g.PendingReinforcements > 0 )
		{
			var target = BestReinforceTarget( g, g.CurrentPlayerId );
			if ( target != null )
			{
				g.Deploy( target, g.PendingReinforcements );
				return;
			}
		}

		g.EndPhase();
	}

	// ----------------------------------------------------------------- attack
	private static void StepAttack( MiskGame g )
	{
		var attack = BestAttack( g, g.CurrentPlayerId );
		if ( attack is { } a )
		{
			int dice = Math.Min( g.Data.Rules.AttackerMaxDice, Math.Max( 1, g.ArmiesOf( a.from ) - 1 ) );
			g.Attack( a.from, a.to, dice );
			return;
		}

		g.EndPhase();
	}

	// ----------------------------------------------------------------- heuristics
	private static string[] FindSet( MiskGame g )
	{
		var cards = g.MyHand.Select( v => new Card( v.Id, v.Kind, v.TerritoryId ) ).ToList();
		var set = CardSetEvaluator.FindSet( cards );
		return set?.Select( c => c.Id ).ToArray();
	}

	// The friendly border territory holding the most armies — concentrate force there.
	private static string BestReinforceTarget( MiskGame g, string me )
	{
		string best = null;
		int bestScore = -1;
		foreach ( var t in g.Map.Territories )
		{
			if ( g.OwnerOf( t.Id ) != me )
				continue;
			bool border = t.AdjacentIds.Any( a => g.OwnerOf( a ) != me );
			int score = g.ArmiesOf( t.Id ) + ( border ? 1000 : 0 );
			if ( score > bestScore )
			{
				bestScore = score;
				best = t.Id;
			}
		}
		return best;
	}

	// The most favourable assault: our strongest edge over an adjacent enemy (only if we outnumber).
	private static (string from, string to)? BestAttack( MiskGame g, string me )
	{
		(string from, string to)? best = null;
		int bestEdge = 0;
		foreach ( var t in g.Map.Territories )
		{
			if ( g.OwnerOf( t.Id ) != me || g.ArmiesOf( t.Id ) < 2 )
				continue;
			foreach ( var adjId in t.AdjacentIds )
			{
				if ( g.OwnerOf( adjId ) == me )
					continue;
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
