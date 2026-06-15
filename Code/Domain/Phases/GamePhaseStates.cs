namespace Misk.Domain;

/// <summary>
/// State pattern: each turn phase decides which commands are legal and what must be true
/// before the player may advance. The TurnController owns the transitions between them.
/// </summary>
public abstract class GamePhaseState
{
	public abstract GamePhase Phase { get; }

	/// <summary>Whether this phase accepts the given command type at all.</summary>
	public abstract bool Accepts( IGameCommand command );

	/// <summary>Called when the phase becomes active for the current player.</summary>
	public virtual void OnEnter( GameContext ctx ) { }

	/// <summary>Whether the player is allowed to leave this phase right now.</summary>
	public virtual bool CanEndPhase( GameContext ctx ) => true;
}

public sealed class ReinforcePhaseState : GamePhaseState
{
	public override GamePhase Phase => GamePhase.Reinforce;

	// Deploy reinforcements or trade in a card set.
	public override bool Accepts( IGameCommand command ) => command is DeployCommand or TradeCardsCommand;

	public override void OnEnter( GameContext ctx )
	{
		ctx.State.CurrentPlayerCapturedThisTurn = false; // new turn for this player
		ctx.SetPhase( GamePhase.Reinforce );
		int reinforcements = ctx.Reinforcements.Calculate( ctx.State, ctx.State.CurrentPlayer.Id );
		ctx.SetPendingReinforcements( reinforcements );
	}

	// Must place every reinforcement first, and must trade if holding too many cards.
	public override bool CanEndPhase( GameContext ctx )
		=> ctx.State.PendingReinforcements <= 0 && !MustTrade( ctx );

	/// <summary>True when the rules force a card trade (hand at/over the limit with a valid set).</summary>
	public static bool MustTrade( GameContext ctx )
	{
		if ( !ctx.Rules.CardsEnabled )
			return false;
		var hand = ctx.State.Hand( ctx.State.CurrentPlayer.Id );
		return hand.Count >= ctx.Rules.MandatoryTradeThreshold && CardSetEvaluator.FindSet( hand ) != null;
	}
}

public sealed class AttackPhaseState : GamePhaseState
{
	public override GamePhase Phase => GamePhase.Attack;

	public override bool Accepts( IGameCommand command ) => command is AttackCommand or AdvanceArmiesCommand;

	public override void OnEnter( GameContext ctx ) => ctx.SetPhase( GamePhase.Attack );

	// Can't leave the attack phase while a capture is still being resolved.
	public override bool CanEndPhase( GameContext ctx ) => ctx.State.PendingAdvance == null;
}

public sealed class FortifyPhaseState : GamePhaseState
{
	public override GamePhase Phase => GamePhase.Fortify;

	public override bool Accepts( IGameCommand command ) => command is FortifyCommand;

	public override void OnEnter( GameContext ctx )
	{
		ctx.State.FortifyMovesUsed = 0;
		ctx.SetPhase( GamePhase.Fortify );
	}
}
