using System;

namespace Misk.Presentation;

/// <summary>Top-level screen the session is on. Synced so every client shows the same screen.</summary>
public enum GameMode
{
	MainMenu,
	Lobby,
	InGame,
	GameOver
}

/// <summary>
/// One player slot. Networked inside a NetList on <see cref="MiskGame"/> (fine-grained sync),
/// so seat/faction/ready changes replicate per-change. <see cref="ConnectionId"/> ties the seat
/// to a network connection (online); in hotseat every seat is host-owned and locally controlled.
/// </summary>
public struct SeatInfo
{
	public string PlayerId;
	public string FactionId;
	public string DisplayName;
	public Guid ConnectionId;
	public bool IsReady;
	public bool IsHuman;
}

/// <summary>A single combat-log entry, built locally on each client from a broadcast result.</summary>
public struct CombatLogLine
{
	public string Message;
	public string AccentHex;
	public bool Captured;
}
