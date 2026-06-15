namespace Misk.Domain;

/// <summary>
/// A participant in a match. Bound to a <see cref="Faction"/> by id. AI is not yet
/// implemented; <see cref="IsHuman"/> is the seam where it will plug in.
/// </summary>
public sealed class Player
{
	public string Id { get; }
	public string FactionId { get; }
	public string DisplayName { get; }
	public bool IsHuman { get; }

	public Player( string id, string factionId, string displayName, bool isHuman )
	{
		Id = id;
		FactionId = factionId;
		DisplayName = displayName;
		IsHuman = isHuman;
	}
}
