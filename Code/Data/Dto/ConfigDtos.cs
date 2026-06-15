using System.Collections.Generic;

namespace Misk.Data;

// DTOs for factions, rules and theme JSON. Kept together as they are small.

public sealed class FactionsDto
{
	public List<FactionDto> Factions { get; set; }
}

public sealed class FactionDto
{
	public string Id { get; set; }
	public string Name { get; set; }
	public string Color { get; set; }
	public string Accent { get; set; }
	public string Sigil { get; set; }
	public string Unit { get; set; }

	/// <summary>Optional per-denomination piece sprites, keyed by unit name (infantry/cavalry/artillery).
	/// Missing keys fall back to <see cref="Unit"/>.</summary>
	public Dictionary<string, string> Units { get; set; }

	public string Glyph { get; set; }
	public string Blurb { get; set; }
}

public sealed class RulesDto
{
	public string Id { get; set; }
	public string Name { get; set; }
	public int MinReinforcements { get; set; }
	public int TerritoriesPerReinforcement { get; set; }
	public int AttackerMaxDice { get; set; }
	public int DefenderMaxDice { get; set; }
	public int MinArmiesToAttack { get; set; }
	public int MinArmiesLeftBehind { get; set; }
	public int FortifyMovesPerTurn { get; set; }
	public bool DefenderAutoMaxDice { get; set; } = true;
	public bool ManualSetup { get; set; } = true;
	public bool CardsEnabled { get; set; } = true;
	public int WildCardCount { get; set; } = 2;
	public List<int> CardSetValues { get; set; }
	public int CardSetIncrement { get; set; } = 5;
	public int CardTerritoryBonus { get; set; } = 2;
	public int MandatoryTradeThreshold { get; set; } = 5;
	public Dictionary<string, int> StartingArmiesByPlayerCount { get; set; }
}

public sealed class ThemeDto
{
	public string Id { get; set; }
	public string Name { get; set; }
	public string MapBackground { get; set; }
	public Dictionary<string, string> Colors { get; set; }
	public Dictionary<string, string> Fonts { get; set; }
	public Dictionary<string, CardStyleDto> Cards { get; set; }

	/// <summary>Optional army-piece denominations for the board (e.g. 10=artillery, 5=cavalry, 1=infantry).</summary>
	public List<ArmyTokenDto> ArmyTokens { get; set; }
}

public sealed class ArmyTokenDto
{
	public int Value { get; set; }
	public string Unit { get; set; }
}

public sealed class CardStyleDto
{
	public string Name { get; set; }
	public string Glyph { get; set; }
	public string Color { get; set; }
}
