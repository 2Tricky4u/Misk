using System.Collections.Generic;

namespace Misk.UI;

/// <summary>
/// Local (non-networked) UI interaction state: hovered/selected territory, the open action
/// menu, deploy/fortify amount, chosen attack dice, and the cards selected for trading.
/// Per-client by design — selection and hover are view concerns, never part of the
/// authoritative game state.
/// </summary>
public static class BoardInteraction
{
	/// <summary>The march/fortify SOURCE the player has committed (via the action menu).</summary>
	public static string SelectedTerritoryId { get; set; }

	/// <summary>The territory under the cursor right now (set each frame by MapView's hit-test).</summary>
	public static string HoveredTerritoryId { get; set; }

	/// <summary>The territory whose context action menu is open, or null when closed.</summary>
	public static string ActionMenuTerritoryId { get; set; }

	public static int MoveAmount { get; set; } = 1;

	/// <summary>How many dice the attacker intends to roll (clamped by the UI to what's legal).</summary>
	public static int AttackDice { get; set; } = 3;

	/// <summary>True while the "max / auto" modifier (Shift) is held — deploy/fortify all, blitz attacks.</summary>
	public static bool MaxModifier { get; set; }

	/// <summary>When true the War Spoils card tray is minimized to a small tab so the board underneath
	/// is clickable. Forced open while a trade is mandatory.</summary>
	public static bool SpoilsCollapsed { get; set; }

	/// <summary>Card ids the player has selected in the card tray (up to three).</summary>
	public static List<string> SelectedCardIds { get; } = new();

	// The board-canvas's on-screen rectangle (physical px), published each frame so sibling
	// overlays (the action menu) can position themselves in canvas space.
	public static float CanvasX { get; set; }
	public static float CanvasY { get; set; }
	public static float CanvasW { get; set; }
	public static float CanvasH { get; set; }

	public static void Clear()
	{
		SelectedTerritoryId = null;
		ActionMenuTerritoryId = null;
	}

	public static void CloseMenu() => ActionMenuTerritoryId = null;

	public static void ToggleCard( string cardId )
	{
		if ( SelectedCardIds.Contains( cardId ) )
			SelectedCardIds.Remove( cardId );
		else if ( SelectedCardIds.Count < 3 )
			SelectedCardIds.Add( cardId );
	}

	public static void ClearCards() => SelectedCardIds.Clear();
}
