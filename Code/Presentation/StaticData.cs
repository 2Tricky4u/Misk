using Misk.Data;

namespace Misk.Presentation;

/// <summary>
/// Per-client cache of the read-only game data (map layout, factions, rules, theme). Loaded
/// once from the JSON files, which are identical on every client, so only dynamic state needs
/// to travel over the network. The host builds a separate, mutable copy for the simulation.
/// </summary>
public static class StaticData
{
	private static GameData _current;

	public static GameData Current
	{
		get
		{
			_current ??= GameDataLoader.Load();
			return _current;
		}
	}

	public static void Reload() => _current = GameDataLoader.Load();
}
