namespace Misk.Domain;

/// <summary>
/// A group of territories (a continent in Risk terms). Owning every territory in
/// a region grants <see cref="Bonus"/> extra reinforcements each turn.
/// </summary>
public sealed class Region
{
	public string Id { get; }
	public string Name { get; }
	public int Bonus { get; }
	public string Color { get; }
	public IReadOnlyList<string> TerritoryIds { get; }

	public Region( string id, string name, int bonus, string color, IReadOnlyList<string> territoryIds )
	{
		Id = id;
		Name = name;
		Bonus = bonus;
		Color = color;
		TerritoryIds = territoryIds;
	}
}
