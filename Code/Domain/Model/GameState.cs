using System.Collections.Generic;

namespace Misk.Domain;

public enum GamePhase
{
	Reinforce,
	Attack,
	Fortify
}

public enum SetupStep
{
	Claim,
	Place
}

/// <summary>A capture awaiting the attacker's choice of how many armies to advance.</summary>
public struct AdvanceRequest
{
	public string FromId;
	public string ToId;
	public int Min;
	public int Max;
}

/// <summary>
/// The full mutable state of one match. Plain serializable C# (no engine types) so it
/// can later be saved/loaded or driven by tests without the visual layer.
/// </summary>
public sealed class GameState
{
	public GameMap Map { get; }
	public RulesConfig Rules { get; }
	public IReadOnlyList<Player> Players { get; }

	public int CurrentPlayerIndex { get; set; }
	public int TurnNumber { get; set; } = 1;
	public GamePhase Phase { get; set; } = GamePhase.Reinforce;

	/// <summary>Armies the current player still has to place during the reinforce phase.</summary>
	public int PendingReinforcements { get; set; }

	/// <summary>Fortify moves already used this turn (reset on entering the fortify phase).</summary>
	public int FortifyMovesUsed { get; set; }

	public string WinnerPlayerId { get; set; }
	public bool IsOver => WinnerPlayerId != null;

	// ---- RISK cards ----
	public Dictionary<string, List<Card>> Hands { get; } = new();
	public CardDeck Deck { get; set; }
	public int CardSetsTradedIn { get; set; }

	/// <summary>Set during a turn once the current player captures a territory (earns a card at turn end).</summary>
	public bool CurrentPlayerCapturedThisTurn { get; set; }

	/// <summary>While set, the attacker must resolve how many armies to advance before doing anything else.</summary>
	public AdvanceRequest? PendingAdvance { get; set; }

	// ---- Manual draft setup ----
	public bool InSetup { get; set; }
	public SetupStep SetupStep { get; set; }
	public Dictionary<string, int> SetupArmiesRemaining { get; } = new();

	public GameState( GameMap map, RulesConfig rules, IReadOnlyList<Player> players )
	{
		Map = map;
		Rules = rules;
		Players = players;
	}

	public Player CurrentPlayer => Players[CurrentPlayerIndex];

	/// <summary>The player's card hand, created empty on first access.</summary>
	public List<Card> Hand( string playerId )
	{
		if ( !Hands.TryGetValue( playerId, out var hand ) )
		{
			hand = new List<Card>();
			Hands[playerId] = hand;
		}
		return hand;
	}

	public Player PlayerById( string id )
	{
		foreach ( var p in Players )
		{
			if ( p.Id == id ) return p;
		}
		return null;
	}
}
