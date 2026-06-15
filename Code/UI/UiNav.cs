namespace Misk.UI;

/// <summary>
/// Local (per-client) UI overlay navigation — which modal sits over the current game Mode
/// (pause / settings / help). Pure view state, never networked.
/// </summary>
public static class UiNav
{
	/// <summary>"pause", "settings", "help", or null when nothing is overlaid.</summary>
	public static string Overlay { get; private set; }

	public static void Show( string overlay ) => Overlay = overlay;
	public static void Close() => Overlay = null;
	public static bool Is( string overlay ) => Overlay == overlay;
}
