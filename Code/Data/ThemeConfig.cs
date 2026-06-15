using System.Collections.Generic;

namespace Misk.Data;

/// <summary>Themed display for one card "suit" (data-driven so cards can be reskinned).</summary>
public sealed class CardStyle
{
	public string Name { get; }
	public string Glyph { get; }
	public string Color { get; }

	public CardStyle( string name, string glyph, string color )
	{
		Name = name;
		Glyph = glyph;
		Color = color;
	}
}

/// <summary>
/// Resolved UI theme. The UI asks for colours/fonts/card-styles by key with a fallback, so a
/// theme can omit values and missing keys never crash rendering. Swap the theme JSON to reskin.
/// </summary>
public sealed class ThemeConfig
{
	public string Id { get; }
	public string Name { get; }
	public string MapBackground { get; }

	private readonly Dictionary<string, string> _colors;
	private readonly Dictionary<string, string> _fonts;
	private readonly Dictionary<string, CardStyle> _cards;

	public ThemeConfig( string id, string name, string mapBackground,
		Dictionary<string, string> colors, Dictionary<string, string> fonts, Dictionary<string, CardStyle> cards )
	{
		Id = id;
		Name = name;
		MapBackground = mapBackground;
		_colors = colors ?? new Dictionary<string, string>();
		_fonts = fonts ?? new Dictionary<string, string>();
		_cards = cards ?? new Dictionary<string, CardStyle>();
	}

	public string Color( string key, string fallback = "#ffffff" )
		=> _colors.TryGetValue( key, out var value ) ? value : fallback;

	public string Font( string key, string fallback = "Georgia" )
		=> _fonts.TryGetValue( key, out var value ) ? value : fallback;

	/// <summary>Card style by suit key ("footmen"/"riders"/"siege"/"banner"); never null.</summary>
	public CardStyle Card( string key )
		=> _cards.TryGetValue( key, out var style ) ? style : new CardStyle( key, "★", "#c9a24a" );

	public static ThemeConfig From( ThemeDto dto )
	{
		var cards = new Dictionary<string, CardStyle>();
		if ( dto.Cards != null )
		{
			foreach ( var kv in dto.Cards )
				cards[kv.Key.ToLowerInvariant()] = new CardStyle( kv.Value.Name, kv.Value.Glyph, kv.Value.Color );
		}
		return new ThemeConfig( dto.Id, dto.Name, dto.MapBackground, dto.Colors, dto.Fonts, cards );
	}

	public static ThemeConfig Fallback()
		=> new ThemeConfig( "fallback", "Fallback", null,
			new Dictionary<string, string>
			{
				{ "parchment", "#c9b28a" }, { "ink", "#2b2118" }, { "panel", "#171210" },
				{ "panelBorder", "#6b5436" }, { "gold", "#c9a24a" }, { "danger", "#8a1c1c" },
				{ "textLight", "#e8dcc0" }, { "textMuted", "#9a8a6a" }
			},
			new Dictionary<string, string> { { "display", "Georgia" }, { "body", "Georgia" } },
			new Dictionary<string, CardStyle>
			{
				{ "footmen", new CardStyle( "Footmen", "⚔", "#9aa0a6" ) },
				{ "riders", new CardStyle( "Riders", "♞", "#c08a4a" ) },
				{ "siege", new CardStyle( "Siege", "⚙", "#8a8580" ) },
				{ "banner", new CardStyle( "Banner", "⚑", "#c9a24a" ) },
			} );
}
