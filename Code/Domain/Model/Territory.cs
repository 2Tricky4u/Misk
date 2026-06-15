namespace Misk.Domain;

/// <summary>
/// A 2D point in map-canvas coordinates (independent of screen resolution).
/// Engine-agnostic so the domain never references Sandbox.Vector2.
/// </summary>
public readonly struct MapPoint
{
	public float X { get; }
	public float Y { get; }

	public MapPoint( float x, float y )
	{
		X = x;
		Y = y;
	}
}

/// <summary>
/// A single conquerable territory. Static shape (id/name/region/adjacency/position/polygon)
/// comes from map data; <see cref="OwnerPlayerId"/> and <see cref="Armies"/> are
/// the mutable runtime board state.
/// </summary>
public sealed class Territory
{
	public string Id { get; }
	public string Name { get; }
	public string RegionId { get; }
	public IReadOnlyList<string> AdjacentIds { get; }

	/// <summary>Anchor point for the army/name badge (canvas coords). Usually inside <see cref="Shape"/>.</summary>
	public MapPoint Position { get; }

	/// <summary>
	/// Polygon outline (canvas coords) of the clickable region cell, or null for a point-only
	/// territory. Used by the presentation layer for shape rendering and point-in-polygon
	/// hit-testing; kept here (pure data + math) so it stays engine-agnostic and testable.
	/// </summary>
	public IReadOnlyList<MapPoint> Shape { get; }

	public bool HasShape => Shape != null && Shape.Count >= 3;

	// Runtime state
	public string OwnerPlayerId { get; set; }
	public int Armies { get; set; }

	public Territory( string id, string name, string regionId, IReadOnlyList<string> adjacentIds, MapPoint position,
		IReadOnlyList<MapPoint> shape = null )
	{
		Id = id;
		Name = name;
		RegionId = regionId;
		AdjacentIds = adjacentIds;
		Position = position;
		Shape = shape;
	}

	public bool IsAdjacentTo( string territoryId ) => AdjacentIds.Contains( territoryId );

	/// <summary>
	/// Ray-casting point-in-polygon test against <see cref="Shape"/> (canvas coords).
	/// False when the territory has no polygon. Edge/vertex hits are not guaranteed either way
	/// (boundary is measure-zero); used for mouse hit-testing where that never matters.
	/// </summary>
	public bool ContainsPoint( MapPoint p )
	{
		if ( !HasShape )
			return false;

		bool inside = false;
		var poly = Shape;
		int n = poly.Count;
		for ( int i = 0, j = n - 1; i < n; j = i++ )
		{
			float xi = poly[i].X, yi = poly[i].Y;
			float xj = poly[j].X, yj = poly[j].Y;

			bool crosses = ( ( yi > p.Y ) != ( yj > p.Y ) ) &&
				( p.X < ( xj - xi ) * ( p.Y - yi ) / ( yj - yi ) + xi );
			if ( crosses )
				inside = !inside;
		}
		return inside;
	}

	/// <summary>Average of the polygon vertices (a stable interior-ish point), or <see cref="Position"/> if no shape.</summary>
	public MapPoint Centroid
	{
		get
		{
			if ( !HasShape )
				return Position;

			float sx = 0f, sy = 0f;
			foreach ( var pt in Shape )
			{
				sx += pt.X;
				sy += pt.Y;
			}
			return new MapPoint( sx / Shape.Count, sy / Shape.Count );
		}
	}
}
