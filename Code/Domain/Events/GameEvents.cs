using System.Collections.Generic;

namespace Misk.Domain;

// Immutable event payloads describing what changed in the GameState. The host raises
// these through the GameEventBus; subscribers replicate them over the network and/or
// refresh the UI. Engine-agnostic by design.

public readonly struct ArmiesChanged
{
	public string TerritoryId { get; }
	public int Armies { get; }
	public ArmiesChanged( string territoryId, int armies ) { TerritoryId = territoryId; Armies = armies; }
}

public readonly struct OwnerChanged
{
	public string TerritoryId { get; }
	public string OwnerPlayerId { get; }
	public string PreviousOwnerPlayerId { get; }
	public OwnerChanged( string territoryId, string ownerPlayerId, string previousOwnerPlayerId )
	{
		TerritoryId = territoryId;
		OwnerPlayerId = ownerPlayerId;
		PreviousOwnerPlayerId = previousOwnerPlayerId;
	}
}

public readonly struct PhaseChanged
{
	public GamePhase Phase { get; }
	public PhaseChanged( GamePhase phase ) { Phase = phase; }
}

public readonly struct TurnChanged
{
	public string PlayerId { get; }
	public int TurnNumber { get; }
	public TurnChanged( string playerId, int turnNumber ) { PlayerId = playerId; TurnNumber = turnNumber; }
}

public readonly struct ReinforcementsChanged
{
	public string PlayerId { get; }
	public int Pending { get; }
	public ReinforcementsChanged( string playerId, int pending ) { PlayerId = playerId; Pending = pending; }
}

public readonly struct GameWon
{
	public string PlayerId { get; }
	public GameWon( string playerId ) { PlayerId = playerId; }
}

/// <summary>A player's card hand changed (drawn/traded/stolen) — host re-delivers it privately.</summary>
public readonly struct HandChanged
{
	public string PlayerId { get; }
	public HandChanged( string playerId ) { PlayerId = playerId; }
}

/// <summary>A player drew a RISK card (for the log; contents stay private).</summary>
public readonly struct CardDrawn
{
	public string PlayerId { get; }
	public CardDrawn( string playerId ) { PlayerId = playerId; }
}

/// <summary>A player traded a card set for armies (for the log).</summary>
public readonly struct CardsTraded
{
	public string PlayerId { get; }
	public int Armies { get; }
	public int TerritoryBonus { get; }
	public CardsTraded( string playerId, int armies, int territoryBonus )
	{
		PlayerId = playerId;
		Armies = armies;
		TerritoryBonus = territoryBonus;
	}
}

/// <summary>The pending-advance state was set or cleared — host re-syncs the advance fields.</summary>
public readonly struct AdvanceChanged { }

/// <summary>Setup step / remaining / current-placer changed — host re-syncs the setup fields.</summary>
public readonly struct SetupChanged { }

/// <summary>A resolved attack, suitable for the combat log and broadcast to clients.</summary>
public sealed class CombatResolved
{
	public string FromTerritoryId { get; init; }
	public string ToTerritoryId { get; init; }
	public string AttackerPlayerId { get; init; }
	public string DefenderPlayerId { get; init; }
	public IReadOnlyList<int> AttackerDice { get; init; }
	public IReadOnlyList<int> DefenderDice { get; init; }
	public int AttackerLosses { get; init; }
	public int DefenderLosses { get; init; }
	public bool Captured { get; init; }
}
