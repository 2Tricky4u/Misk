namespace Misk.Domain;

/// <summary>
/// A playable faction. Purely cosmetic/identity data — no rules live here, so new
/// factions can be added through data alone. <see cref="SigilPath"/> is optional;
/// <see cref="Glyph"/> is a placeholder symbol used until real sigil art exists.
/// </summary>
public sealed class Faction
{
	public string Id { get; }
	public string Name { get; }
	public string Color { get; }
	public string Accent { get; }
	public string SigilPath { get; }

	/// <summary>Optional full-body unit/pawn sprite for the board (transparent PNG). Falls back to the glyph.</summary>
	public string UnitPath { get; }
	public string Glyph { get; }
	public string Blurb { get; }

	public Faction( string id, string name, string color, string accent, string sigilPath, string unitPath, string glyph, string blurb )
	{
		Id = id;
		Name = name;
		Color = color;
		Accent = accent;
		SigilPath = sigilPath;
		UnitPath = unitPath;
		Glyph = glyph;
		Blurb = blurb;
	}
}
