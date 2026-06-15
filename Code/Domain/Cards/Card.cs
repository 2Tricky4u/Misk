namespace Misk.Domain;

/// <summary>
/// The kind ("suit") of a RISK card. The first three mirror the classic Infantry/Cavalry/
/// Artillery; <see cref="Banner"/> is the wild card. Display name/glyph/colour are themed
/// in the theme config — logic only ever uses this enum.
/// </summary>
public enum CardKind
{
	Footmen,
	Riders,
	Siege,
	Banner
}

/// <summary>
/// A single RISK card. Troop cards (Footmen/Riders/Siege) depict a territory; the Banner
/// (wild) has no territory. Earned by capturing during a turn, collected into sets, and
/// traded for reinforcements.
/// </summary>
public sealed class Card
{
	public string Id { get; }
	public CardKind Kind { get; }

	/// <summary>The depicted territory id, or null for a Banner (wild) card.</summary>
	public string TerritoryId { get; }

	public bool IsWild => Kind == CardKind.Banner;

	public Card( string id, CardKind kind, string territoryId )
	{
		Id = id;
		Kind = kind;
		TerritoryId = territoryId;
	}
}
