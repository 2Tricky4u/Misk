using System.Collections.Generic;

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

	/// <summary>Optional full-body unit/pawn sprite for the board (transparent PNG). Falls back to the glyph.
	/// This is the infantry/default piece; <see cref="UnitFor"/> resolves per-denomination art.</summary>
	public string UnitPath { get; }

	/// <summary>Per-denomination piece sprites keyed by unit name (infantry/cavalry/artillery). May be empty.</summary>
	public IReadOnlyDictionary<string, string> Units { get; }

	public string Glyph { get; }
	public string Blurb { get; }

	public Faction( string id, string name, string color, string accent, string sigilPath, string unitPath, string glyph, string blurb,
		IReadOnlyDictionary<string, string> units = null )
	{
		Id = id;
		Name = name;
		Color = color;
		Accent = accent;
		SigilPath = sigilPath;
		UnitPath = unitPath;
		Units = units ?? new Dictionary<string, string>();
		Glyph = glyph;
		Blurb = blurb;
	}

	/// <summary>The piece sprite for a denomination's unit name, falling back to the default <see cref="UnitPath"/>.</summary>
	public string UnitFor( string unitKey )
	{
		if ( !string.IsNullOrEmpty( unitKey ) && Units.TryGetValue( unitKey, out var path ) && !string.IsNullOrEmpty( path ) )
			return path;
		return UnitPath;
	}
}
