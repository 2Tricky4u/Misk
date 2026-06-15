namespace Misk.UI;

/// <summary>
/// Local (per-client) UI overlay navigation — which modal sits over the current game Mode
/// (pause / settings / help). Pure view state, never networked.
/// </summary>
public static class UiNav
{
	/// <summary>"pause", "settings", "help", "confirm", or null when nothing is overlaid.</summary>
	public static string Overlay { get; private set; }

	public static void Show( string overlay ) => Overlay = overlay;
	public static void Close() => Overlay = null;
	public static bool Is( string overlay ) => Overlay == overlay;

	// ---- Confirmation dialog: a prompt plus the action to run if the player confirms.
	public static string ConfirmTitle { get; private set; }
	public static string ConfirmBody { get; private set; }
	public static string ConfirmVerb { get; private set; }
	private static System.Action _confirmAction;

	/// <summary>Raise a yes/no dialog. <paramref name="onConfirm"/> runs only if the player accepts.</summary>
	public static void Confirm( string title, string body, string verb, System.Action onConfirm )
	{
		ConfirmTitle = title;
		ConfirmBody = body;
		ConfirmVerb = verb;
		_confirmAction = onConfirm;
		Overlay = "confirm";
	}

	public static void AcceptConfirm()
	{
		var action = _confirmAction;
		_confirmAction = null;
		Overlay = null;
		action?.Invoke();
	}

	public static void CancelConfirm()
	{
		_confirmAction = null;
		Overlay = null;
	}
}
