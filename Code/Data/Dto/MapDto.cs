using System.Collections.Generic;

namespace Misk.Data;

// Plain data-transfer objects matching the map JSON shape. Property names map to the
// camelCase JSON keys via case-insensitive deserialization. These never leak into the
// domain — the loader converts them into validated Misk.Domain models.

public sealed class MapDto
{
	public string Id { get; set; }
	public string Name { get; set; }
	public int Version { get; set; }
	public string Background { get; set; }
	public CanvasSizeDto CanvasSize { get; set; }
	public List<RegionDto> Regions { get; set; }
	public List<TerritoryDto> Territories { get; set; }
}

public sealed class CanvasSizeDto
{
	public float Width { get; set; }
	public float Height { get; set; }
}

public sealed class RegionDto
{
	public string Id { get; set; }
	public string Name { get; set; }
	public int Bonus { get; set; }
	public string Color { get; set; }
}

public sealed class TerritoryDto
{
	public string Id { get; set; }
	public string Name { get; set; }
	public string Region { get; set; }
	public PositionDto Position { get; set; }
	public List<PositionDto> Shape { get; set; } // optional polygon outline (canvas coords) of the region cell
	public List<string> Adjacency { get; set; }
}

public sealed class PositionDto
{
	public float X { get; set; }
	public float Y { get; set; }
}
