using System.Collections.Generic;
using System.Linq;

namespace Misk.Domain;

/// <summary>
/// The board: all territories and regions plus convenience queries. Construction is
/// expected to receive already-validated data (see Misk.Data.MapValidator). Holds no
/// turn/phase state — that lives in <see cref="GameState"/>.
/// </summary>
public sealed class GameMap
{
	private readonly Dictionary<string, Territory> _territories;
	private readonly Dictionary<string, Region> _regions;

	public string Id { get; }
	public string Name { get; }
	public float CanvasWidth { get; }
	public float CanvasHeight { get; }
	public string BackgroundPath { get; }

	public IReadOnlyCollection<Territory> Territories => _territories.Values;
	public IReadOnlyCollection<Region> Regions => _regions.Values;

	public GameMap( string id, string name, float canvasWidth, float canvasHeight, string backgroundPath,
		IEnumerable<Territory> territories, IEnumerable<Region> regions )
	{
		Id = id;
		Name = name;
		CanvasWidth = canvasWidth;
		CanvasHeight = canvasHeight;
		BackgroundPath = backgroundPath;
		_territories = territories.ToDictionary( t => t.Id );
		_regions = regions.ToDictionary( r => r.Id );
	}

	public Territory Territory( string id ) => _territories.TryGetValue( id, out var t ) ? t : null;
	public Region Region( string id ) => _regions.TryGetValue( id, out var r ) ? r : null;

	public bool AreAdjacent( string a, string b )
	{
		var t = Territory( a );
		return t != null && t.IsAdjacentTo( b );
	}

	public IEnumerable<Territory> TerritoriesInRegion( string regionId )
		=> _territories.Values.Where( t => t.RegionId == regionId );

	public IEnumerable<Territory> OwnedBy( string playerId )
		=> _territories.Values.Where( t => t.OwnerPlayerId == playerId );

	public int CountOwnedBy( string playerId )
		=> _territories.Values.Count( t => t.OwnerPlayerId == playerId );

	/// <summary>True if every territory in the region is owned by the given player.</summary>
	public bool RegionFullyOwnedBy( string regionId, string playerId )
	{
		var region = Region( regionId );
		if ( region == null ) return false;
		return region.TerritoryIds.All( id => Territory( id )?.OwnerPlayerId == playerId );
	}

	/// <summary>Distinct player ids that currently own at least one territory.</summary>
	public IEnumerable<string> ActiveOwners()
		=> _territories.Values.Select( t => t.OwnerPlayerId ).Where( id => id != null ).Distinct();
}
