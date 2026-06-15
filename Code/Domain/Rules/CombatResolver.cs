using System;
using System.Collections.Generic;
using System.Linq;

namespace Misk.Domain;

/// <summary>The outcome of a single dice exchange. Pure data — no state is mutated here.</summary>
public sealed class CombatOutcome
{
	public IReadOnlyList<int> AttackerDice { get; }
	public IReadOnlyList<int> DefenderDice { get; }
	public int AttackerLosses { get; }
	public int DefenderLosses { get; }

	public CombatOutcome( IReadOnlyList<int> attackerDice, IReadOnlyList<int> defenderDice, int attackerLosses, int defenderLosses )
	{
		AttackerDice = attackerDice;
		DefenderDice = defenderDice;
		AttackerLosses = attackerLosses;
		DefenderLosses = defenderLosses;
	}
}

/// <summary>Strategy for resolving one attacker-vs-defender dice exchange. Swap for variants.</summary>
public interface ICombatResolver
{
	/// <summary>
	/// Resolve one exchange. <paramref name="requestedAttackerDice"/> is the attacker's chosen
	/// dice count, clamped to what the rules and army count allow; the defender auto-rolls the
	/// most legal dice.
	/// </summary>
	CombatOutcome Resolve( int attackerArmies, int defenderArmies, int requestedAttackerDice, RulesConfig rules, IDiceRoller dice );
}

/// <summary>
/// Classic Risk combat: attacker rolls the chosen number of dice (never more than armies-1 or
/// the rules max), defender rolls up to DefenderMaxDice. Highest dice are compared pairwise; the
/// defender wins ties.
/// </summary>
public sealed class DiceCombatResolver : ICombatResolver
{
	public CombatOutcome Resolve( int attackerArmies, int defenderArmies, int requestedAttackerDice, RulesConfig rules, IDiceRoller dice )
	{
		int maxAttackerDice = Math.Min( rules.AttackerMaxDice, Math.Max( 0, attackerArmies - 1 ) );
		int attackerDiceCount = Math.Clamp( requestedAttackerDice, Math.Min( 1, maxAttackerDice ), maxAttackerDice );
		int defenderDiceCount = Math.Min( rules.DefenderMaxDice, Math.Max( 0, defenderArmies ) );

		var attackerRolls = RollSortedDescending( attackerDiceCount, dice );
		var defenderRolls = RollSortedDescending( defenderDiceCount, dice );

		int comparisons = Math.Min( attackerDiceCount, defenderDiceCount );
		int attackerLosses = 0;
		int defenderLosses = 0;

		for ( int i = 0; i < comparisons; i++ )
		{
			// Defender wins ties — a core Risk rule.
			if ( attackerRolls[i] > defenderRolls[i] )
				defenderLosses++;
			else
				attackerLosses++;
		}

		return new CombatOutcome( attackerRolls, defenderRolls, attackerLosses, defenderLosses );
	}

	private static List<int> RollSortedDescending( int count, IDiceRoller dice )
	{
		var rolls = new List<int>( count );
		for ( int i = 0; i < count; i++ )
			rolls.Add( dice.Roll() );
		rolls.Sort( ( a, b ) => b.CompareTo( a ) );
		return rolls;
	}
}
