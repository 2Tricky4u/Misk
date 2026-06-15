using System.Collections.Generic;
using System.Linq;

namespace Misk.Domain;

/// <summary>
/// Pure rules for RISK card sets: what counts as a valid set of three, finding a tradeable
/// set in a hand, and the escalating army value of the next set traded. No state, no engine
/// types — trivially testable.
/// </summary>
public static class CardSetEvaluator
{
	/// <summary>
	/// A valid set is three cards that are all the same troop kind, one of each troop kind,
	/// or any combination containing at least one Banner (wild).
	/// </summary>
	public static bool IsValidSet( Card a, Card b, Card c )
	{
		if ( a == null || b == null || c == null )
			return false;

		if ( a.IsWild || b.IsWild || c.IsWild )
			return true;

		bool allSame = a.Kind == b.Kind && b.Kind == c.Kind;
		bool allDifferent = a.Kind != b.Kind && b.Kind != c.Kind && a.Kind != c.Kind;
		return allSame || allDifferent;
	}

	/// <summary>Returns the first valid 3-card set found in the hand, or null if none exists.</summary>
	public static IReadOnlyList<Card> FindSet( IReadOnlyList<Card> hand )
	{
		if ( hand == null || hand.Count < 3 )
			return null;

		for ( int i = 0; i < hand.Count - 2; i++ )
		for ( int j = i + 1; j < hand.Count - 1; j++ )
		for ( int k = j + 1; k < hand.Count; k++ )
		{
			if ( IsValidSet( hand[i], hand[j], hand[k] ) )
				return new[] { hand[i], hand[j], hand[k] };
		}

		return null;
	}

	/// <summary>
	/// Army value of a traded set given how many sets have already been traded in the game.
	/// Follows the configured ladder (4,6,8,10,12,15) then increases by the increment each time.
	/// </summary>
	public static int SetValue( int setsTradedInSoFar, RulesConfig rules )
	{
		var ladder = rules.CardSetValues;
		if ( ladder == null || ladder.Count == 0 )
			return 4 + setsTradedInSoFar * 2;

		if ( setsTradedInSoFar < ladder.Count )
			return ladder[setsTradedInSoFar];

		int stepsBeyond = setsTradedInSoFar - (ladder.Count - 1);
		return ladder[ladder.Count - 1] + stepsBeyond * rules.CardSetIncrement;
	}
}
