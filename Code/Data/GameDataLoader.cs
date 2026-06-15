using System.Collections.Generic;
using System.Linq;
using Misk.Domain;

namespace Misk.Data;

/// <summary>The fully-loaded, validated set of data needed to start a match.</summary>
public sealed class GameData
{
	public GameMap Map { get; init; }
	public IReadOnlyList<Faction> Factions { get; init; }
	public RulesConfig Rules { get; init; }
	public ThemeConfig Theme { get; init; }
	public IReadOnlyList<string> Warnings { get; init; }

	public Faction Faction( string id ) => Factions.FirstOrDefault( f => f.Id == id );
}

/// <summary>
/// Reads the JSON config (map/factions/rules/theme) from the mounted filesystem, validates
/// it, and converts it into engine-agnostic domain models. This is the only bridge between
/// raw data files and the game — change the files (or the paths) to change the world.
/// </summary>
public static class GameDataLoader
{
	public const string DefaultMapPath = "data/maps/sundering_realms.json";
	public const string DefaultFactionsPath = "data/factions/factions.json";
	public const string DefaultRulesPath = "data/rules/classic.json";
	public const string DefaultThemePath = "data/theme/dark_atlas.json";

	public static GameData Load(
		string mapPath = DefaultMapPath,
		string factionsPath = DefaultFactionsPath,
		string rulesPath = DefaultRulesPath,
		string themePath = DefaultThemePath )
	{
		var mapDto = ReadJson<MapDto>( mapPath );
		var warnings = MapValidator.Validate( mapDto );
		var map = BuildMap( mapDto );

		var factionsDto = ReadJson<FactionsDto>( factionsPath );
		var factions = BuildFactions( factionsDto );

		var rulesDto = ReadJson<RulesDto>( rulesPath );
		var rules = BuildRules( rulesDto );

		var themeDto = ReadJson<ThemeDto>( themePath );
		var theme = themeDto != null ? ThemeConfig.From( themeDto ) : ThemeConfig.Fallback();

		return new GameData
		{
			Map = map,
			Factions = factions,
			Rules = rules,
			Theme = theme,
			Warnings = warnings,
		};
	}

	private static T ReadJson<T>( string path ) where T : class
	{
		if ( !FileSystem.Mounted.FileExists( path ) )
			throw new System.IO.FileNotFoundException( $"Game data file not found: {path}" );

		var json = FileSystem.Mounted.ReadAllText( path );
		var result = Json.Deserialize<T>( json );
		if ( result == null )
			throw new System.Exception( $"Failed to parse game data file: {path}" );

		return result;
	}

	private static GameMap BuildMap( MapDto dto )
	{
		var territories = dto.Territories.Select( t => new Territory(
			t.Id,
			t.Name,
			t.Region,
			t.Adjacency ?? new List<string>(),
			new MapPoint( t.Position.X, t.Position.Y ),
			BuildShape( t.Shape ) ) ).ToList();

		// Region territory lists derive from the territories themselves (single source of truth).
		var territoryIdsByRegion = territories
			.GroupBy( t => t.RegionId )
			.ToDictionary( g => g.Key, g => g.Select( t => t.Id ).ToList() );

		var regions = dto.Regions.Select( r => new Region(
			r.Id,
			r.Name,
			r.Bonus,
			r.Color,
			territoryIdsByRegion.TryGetValue( r.Id, out var ids ) ? ids : new List<string>() ) ).ToList();

		return new GameMap(
			dto.Id,
			dto.Name,
			dto.CanvasSize.Width,
			dto.CanvasSize.Height,
			dto.Background,
			territories,
			regions );
	}

	/// <summary>Converts the optional JSON polygon point-list into engine-agnostic MapPoints (null if absent/degenerate).</summary>
	private static List<MapPoint> BuildShape( List<PositionDto> shape )
	{
		if ( shape == null || shape.Count < 3 )
			return null;
		return shape.Select( p => new MapPoint( p.X, p.Y ) ).ToList();
	}

	private static List<Faction> BuildFactions( FactionsDto dto )
	{
		return (dto.Factions ?? new List<FactionDto>()).Select( f => new Faction(
			f.Id, f.Name, f.Color, f.Accent, f.Sigil, f.Unit, f.Glyph, f.Blurb, BuildUnits( f ) ) ).ToList();
	}

	// Per-denomination piece sprites, lower-cased keys. The infantry/default piece falls back to the
	// legacy single "unit" sprite so existing factions keep working without a "units" block.
	private static Dictionary<string, string> BuildUnits( FactionDto f )
	{
		var units = new Dictionary<string, string>();
		if ( f.Units != null )
		{
			foreach ( var kv in f.Units )
				if ( !string.IsNullOrEmpty( kv.Value ) )
					units[kv.Key.ToLowerInvariant()] = kv.Value;
		}
		if ( !units.ContainsKey( "infantry" ) && !string.IsNullOrEmpty( f.Unit ) )
			units["infantry"] = f.Unit;
		return units;
	}

	private static RulesConfig BuildRules( RulesDto dto )
	{
		var startingArmies = new Dictionary<int, int>();
		if ( dto.StartingArmiesByPlayerCount != null )
		{
			foreach ( var kv in dto.StartingArmiesByPlayerCount )
			{
				if ( int.TryParse( kv.Key, out var playerCount ) )
					startingArmies[playerCount] = kv.Value;
			}
		}

		var cardSetValues = (dto.CardSetValues != null && dto.CardSetValues.Count > 0)
			? dto.CardSetValues.ToArray()
			: new[] { 4, 6, 8, 10, 12, 15 };

		return new RulesConfig
		{
			Id = dto.Id ?? "classic",
			Name = dto.Name ?? "Classic Conquest",
			MinReinforcements = dto.MinReinforcements,
			TerritoriesPerReinforcement = System.Math.Max( 1, dto.TerritoriesPerReinforcement ),
			AttackerMaxDice = System.Math.Max( 1, dto.AttackerMaxDice ),
			DefenderMaxDice = System.Math.Max( 1, dto.DefenderMaxDice ),
			MinArmiesToAttack = System.Math.Max( 2, dto.MinArmiesToAttack ),
			MinArmiesLeftBehind = System.Math.Max( 1, dto.MinArmiesLeftBehind ),
			FortifyMovesPerTurn = System.Math.Max( 1, dto.FortifyMovesPerTurn ),
			DefenderAutoMaxDice = dto.DefenderAutoMaxDice,
			ManualSetup = dto.ManualSetup,
			CardsEnabled = dto.CardsEnabled,
			WildCardCount = System.Math.Max( 0, dto.WildCardCount ),
			CardSetValues = cardSetValues,
			CardSetIncrement = System.Math.Max( 1, dto.CardSetIncrement ),
			CardTerritoryBonus = System.Math.Max( 0, dto.CardTerritoryBonus ),
			MandatoryTradeThreshold = System.Math.Max( 3, dto.MandatoryTradeThreshold ),
			StartingArmiesByPlayerCount = startingArmies.Count > 0 ? startingArmies : null,
		};
	}
}
