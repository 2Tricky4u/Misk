using System.Collections.Generic;

namespace Misk.Domain;

/// <summary>
/// All tunable rule numbers, loaded from data (e.g. rules/classic.json). Keeping these
/// out of the logic lets you create rule variants without touching code.
/// </summary>
public sealed class RulesConfig
{
	public string Id { get; init; } = "classic";
	public string Name { get; init; } = "Classic Conquest";

	public int MinReinforcements { get; init; } = 3;
	public int TerritoriesPerReinforcement { get; init; } = 3;

	public int AttackerMaxDice { get; init; } = 3;
	public int DefenderMaxDice { get; init; } = 2;

	/// <summary>Minimum armies a territory needs before it may attack (it always leaves at least one behind).</summary>
	public int MinArmiesToAttack { get; init; } = 2;

	/// <summary>Armies that must remain in a territory after fortifying out of it.</summary>
	public int MinArmiesLeftBehind { get; init; } = 1;

	public int FortifyMovesPerTurn { get; init; } = 1;

	/// <summary>Defender automatically rolls the most legal dice. (Interactive defence is a future variant.)</summary>
	public bool DefenderAutoMaxDice { get; init; } = true;

	/// <summary>Players claim territories and place starting armies by hand. When false, GameSetup auto-assigns.</summary>
	public bool ManualSetup { get; init; } = true;

	// ---- RISK cards ----
	public bool CardsEnabled { get; init; } = true;
	public int WildCardCount { get; init; } = 2;

	/// <summary>Escalating army value of the 1st, 2nd, ... set traded in the game.</summary>
	public IReadOnlyList<int> CardSetValues { get; init; } = new[] { 4, 6, 8, 10, 12, 15 };

	/// <summary>Added to the last ladder value for every set beyond the ladder length.</summary>
	public int CardSetIncrement { get; init; } = 5;

	/// <summary>Extra armies for trading a card that depicts a territory you own (placed there).</summary>
	public int CardTerritoryBonus { get; init; } = 2;

	/// <summary>If you hold this many cards at the start of your turn, you must trade a set.</summary>
	public int MandatoryTradeThreshold { get; init; } = 5;

	/// <summary>Total starting armies per player, keyed by player count.</summary>
	public IReadOnlyDictionary<int, int> StartingArmiesByPlayerCount { get; init; }
		= new Dictionary<int, int> { { 2, 40 }, { 3, 35 }, { 4, 30 }, { 5, 25 }, { 6, 20 } };

	public int StartingArmiesFor( int playerCount )
	{
		if ( StartingArmiesByPlayerCount != null && StartingArmiesByPlayerCount.TryGetValue( playerCount, out var n ) )
			return n;
		// Fallback formula if the table has no entry for this count.
		return System.Math.Max( 20, 50 - playerCount * 5 );
	}
}
