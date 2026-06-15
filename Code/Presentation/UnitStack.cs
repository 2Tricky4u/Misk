using System.Collections.Generic;

namespace Misk.Presentation;

/// <summary>
/// Turns a flat army count into a stack of denominated pieces for the board, classic-Risk style
/// (e.g. 17 → one artillery (10) + one cavalry (5) + two infantry (1)). Pure display logic — the
/// domain keeps a single <c>Armies</c> int per territory; this only decides which pieces to draw.
/// The denomination table comes from the theme (<c>ThemeConfig.ArmyTokens</c>, sorted descending
/// with a guaranteed value-1 piece), so the result always sums back to the original count.
/// </summary>
public static class UnitStack
{
	/// <summary>One denomination's contribution to a garrison: its unit art key, face value, and how many.</summary>
	public readonly struct Token
	{
		public string UnitKey { get; }
		public int Denomination { get; }
		public int Count { get; }

		public Token( string unitKey, int denomination, int count )
		{
			UnitKey = unitKey;
			Denomination = denomination;
			Count = count;
		}
	}

	/// <summary>Greedy decomposition of <paramref name="armies"/> over a descending denomination table.</summary>
	public static IReadOnlyList<Token> Decompose( int armies, IReadOnlyList<(int Value, string Unit)> tokens )
	{
		var result = new List<Token>();
		if ( armies <= 0 || tokens == null || tokens.Count == 0 )
			return result;

		int remaining = armies;
		foreach ( var (value, unit) in tokens )
		{
			if ( value <= 0 )
				continue;

			int count = remaining / value;
			if ( count > 0 )
			{
				result.Add( new Token( unit, value, count ) );
				remaining -= count * value;
			}

			if ( remaining <= 0 )
				break;
		}
		return result;
	}
}
