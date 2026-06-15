using System;
using System.Collections.Generic;
using System.Linq;

namespace Misk.Data;

/// <summary>Thrown when map data is structurally invalid. Message lists every problem found.</summary>
public sealed class MapValidationException : Exception
{
	public IReadOnlyList<string> Errors { get; }

	public MapValidationException( IReadOnlyList<string> errors )
		: base( "Map data is invalid:\n - " + string.Join( "\n - ", errors ) )
	{
		Errors = errors;
	}
}

/// <summary>
/// Validates a <see cref="MapDto"/> before it becomes a playable map. Catches the mistakes
/// that would otherwise produce a broken or unwinnable board: bad references, duplicate ids,
/// asymmetric adjacency, empty regions, out-of-bounds positions. Errors throw; softer issues
/// are returned as warnings.
/// </summary>
public static class MapValidator
{
	public static IReadOnlyList<string> Validate( MapDto map )
	{
		var errors = new List<string>();
		var warnings = new List<string>();

		if ( map == null )
			throw new MapValidationException( new[] { "Map failed to parse (null)." } );

		if ( string.IsNullOrWhiteSpace( map.Id ) ) errors.Add( "Map is missing an id." );
		if ( map.CanvasSize == null || map.CanvasSize.Width <= 0 || map.CanvasSize.Height <= 0 )
			errors.Add( "Map canvasSize must have positive width and height." );

		var regions = map.Regions ?? new List<RegionDto>();
		var territories = map.Territories ?? new List<TerritoryDto>();

		if ( regions.Count == 0 ) errors.Add( "Map has no regions." );
		if ( territories.Count == 0 ) errors.Add( "Map has no territories." );

		// Duplicate / missing ids.
		var regionIds = new HashSet<string>();
		foreach ( var r in regions )
		{
			if ( string.IsNullOrWhiteSpace( r.Id ) ) { errors.Add( "A region is missing an id." ); continue; }
			if ( !regionIds.Add( r.Id ) ) errors.Add( $"Duplicate region id '{r.Id}'." );
		}

		var territoryIds = new HashSet<string>();
		foreach ( var t in territories )
		{
			if ( string.IsNullOrWhiteSpace( t.Id ) ) { errors.Add( "A territory is missing an id." ); continue; }
			if ( !territoryIds.Add( t.Id ) ) errors.Add( $"Duplicate territory id '{t.Id}'." );
		}

		// Per-territory checks: region ref, position bounds, adjacency refs, self-links.
		foreach ( var t in territories )
		{
			if ( string.IsNullOrWhiteSpace( t.Id ) ) continue;

			if ( string.IsNullOrWhiteSpace( t.Region ) || !regionIds.Contains( t.Region ) )
				errors.Add( $"Territory '{t.Id}' references unknown region '{t.Region}'." );

			if ( t.Position == null )
			{
				errors.Add( $"Territory '{t.Id}' has no position." );
			}
			else if ( map.CanvasSize != null )
			{
				if ( t.Position.X < 0 || t.Position.X > map.CanvasSize.Width ||
					 t.Position.Y < 0 || t.Position.Y > map.CanvasSize.Height )
					warnings.Add( $"Territory '{t.Id}' position is outside the canvas bounds." );
			}

			foreach ( var adj in t.Adjacency ?? new List<string>() )
			{
				if ( adj == t.Id )
					errors.Add( $"Territory '{t.Id}' lists itself as adjacent." );
				else if ( !territoryIds.Contains( adj ) )
					errors.Add( $"Territory '{t.Id}' is adjacent to unknown territory '{adj}'." );
			}

			// Optional polygon "shape": a region cell needs at least a triangle, and should sit
			// on the canvas. Missing shapes are fine (the territory just falls back to a point marker).
			if ( t.Shape != null )
			{
				if ( t.Shape.Count < 3 )
				{
					errors.Add( $"Territory '{t.Id}' shape has fewer than 3 points." );
				}
				else if ( map.CanvasSize != null )
				{
					foreach ( var pt in t.Shape )
					{
						if ( pt == null ) { errors.Add( $"Territory '{t.Id}' shape has a null point." ); break; }
						if ( pt.X < 0 || pt.X > map.CanvasSize.Width || pt.Y < 0 || pt.Y > map.CanvasSize.Height )
						{
							warnings.Add( $"Territory '{t.Id}' shape extends outside the canvas bounds." );
							break;
						}
					}
				}
			}
		}

		// Adjacency must be symmetric (a->b implies b->a).
		var adjacency = territories
			.Where( t => !string.IsNullOrWhiteSpace( t.Id ) )
			.ToDictionary( t => t.Id, t => new HashSet<string>( t.Adjacency ?? new List<string>() ) );

		foreach ( var t in territories )
		{
			if ( string.IsNullOrWhiteSpace( t.Id ) ) continue;
			foreach ( var adj in adjacency[t.Id] )
			{
				if ( territoryIds.Contains( adj ) && !adjacency[adj].Contains( t.Id ) )
					errors.Add( $"Adjacency is not symmetric: '{t.Id}' -> '{adj}' but not back." );
			}
		}

		// Every region should contain at least one territory.
		var territoriesByRegion = territories
			.Where( t => !string.IsNullOrWhiteSpace( t.Region ) )
			.GroupBy( t => t.Region )
			.ToDictionary( g => g.Key, g => g.Count() );

		foreach ( var r in regions )
		{
			if ( !string.IsNullOrWhiteSpace( r.Id ) && !territoriesByRegion.ContainsKey( r.Id ) )
				errors.Add( $"Region '{r.Id}' contains no territories." );
		}

		// Connectivity: a disconnected board can be unwinnable — warn rather than block.
		if ( errors.Count == 0 && territories.Count > 0 && !IsConnected( adjacency ) )
			warnings.Add( "Map graph is not fully connected; some territories may be unreachable." );

		if ( errors.Count > 0 )
			throw new MapValidationException( errors );

		return warnings;
	}

	private static bool IsConnected( Dictionary<string, HashSet<string>> adjacency )
	{
		if ( adjacency.Count == 0 ) return true;

		var visited = new HashSet<string>();
		var queue = new Queue<string>();
		var start = adjacency.Keys.First();
		queue.Enqueue( start );
		visited.Add( start );

		while ( queue.Count > 0 )
		{
			var current = queue.Dequeue();
			foreach ( var next in adjacency[current] )
			{
				if ( adjacency.ContainsKey( next ) && visited.Add( next ) )
					queue.Enqueue( next );
			}
		}

		return visited.Count == adjacency.Count;
	}
}
