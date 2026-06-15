using System.Collections.Generic;

namespace Misk.Domain;

/// <summary>
/// Host-side orchestrator. Owns the active <see cref="GamePhaseState"/>, routes commands
/// through phase + command validation, drives phase/turn transitions, and checks victory
/// after every mutation. This is the only object that advances the game.
/// </summary>
public sealed class TurnController
{
	private readonly GameContext _ctx;
	private readonly Dictionary<GamePhase, GamePhaseState> _phases;
	private GamePhaseState _current;

	public GamePhase CurrentPhase => _current.Phase;

	public TurnController( GameContext ctx )
	{
		_ctx = ctx;
		_phases = new Dictionary<GamePhase, GamePhaseState>
		{
			{ GamePhase.Reinforce, new ReinforcePhaseState() },
			{ GamePhase.Attack, new AttackPhaseState() },
			{ GamePhase.Fortify, new FortifyPhaseState() },
		};
		_current = _phases[GamePhase.Reinforce];
	}

	/// <summary>Begin the match: first player enters the reinforce phase.</summary>
	public void StartGame()
	{
		_ctx.State.CurrentPlayerIndex = 0;
		_ctx.State.TurnNumber = 1;
		Enter( GamePhase.Reinforce );
		_ctx.Events.Raise( new TurnChanged( _ctx.State.CurrentPlayer.Id, _ctx.State.TurnNumber ) );
	}

	/// <summary>Validate and run a player command. Safe to call from network request handlers.</summary>
	public CommandResult Execute( IGameCommand command )
	{
		if ( _ctx.State.IsOver )
			return CommandResult.Fail( "The game is over." );

		// A pending capture must be resolved before anything else happens.
		if ( _ctx.State.PendingAdvance != null && command is not AdvanceArmiesCommand )
			return CommandResult.Fail( "Resolve the captured territory first." );

		if ( !_current.Accepts( command ) )
			return CommandResult.Fail( $"That action is not allowed during the {_current.Phase} phase." );

		var validation = command.Validate( _ctx );
		if ( !validation.Ok )
			return validation;

		command.Execute( _ctx );
		CheckVictory();
		return CommandResult.Success;
	}

	/// <summary>Advance the current player past the active phase (or end their turn).</summary>
	public CommandResult EndPhase( string playerId )
	{
		if ( _ctx.State.IsOver )
			return CommandResult.Fail( "The game is over." );

		if ( _ctx.State.CurrentPlayer.Id != playerId )
			return CommandResult.Fail( "It is not your turn." );

		if ( !_current.CanEndPhase( _ctx ) )
			return CommandResult.Fail( "You must finish this phase first." );

		switch ( _current.Phase )
		{
			case GamePhase.Reinforce:
				Enter( GamePhase.Attack );
				break;
			case GamePhase.Attack:
				Enter( GamePhase.Fortify );
				break;
			case GamePhase.Fortify:
				EndTurn();
				break;
		}

		return CommandResult.Success;
	}

	private void EndTurn()
	{
		// Earn one card if you captured at least one territory this turn (rulebook).
		if ( _ctx.Rules.CardsEnabled && _ctx.State.CurrentPlayerCapturedThisTurn )
			_ctx.DrawCard( _ctx.State.CurrentPlayer.Id );

		AdvanceToNextActivePlayer();
		Enter( GamePhase.Reinforce );
		_ctx.Events.Raise( new TurnChanged( _ctx.State.CurrentPlayer.Id, _ctx.State.TurnNumber ) );
	}

	private void Enter( GamePhase phase )
	{
		_current = _phases[phase];
		_current.OnEnter( _ctx );
	}

	private void AdvanceToNextActivePlayer()
	{
		var state = _ctx.State;
		int count = state.Players.Count;
		int previous = state.CurrentPlayerIndex;

		for ( int step = 0; step < count; step++ )
		{
			int idx = (previous + 1 + step) % count;
			// Skip players that have been eliminated (own no territories).
			if ( state.Map.CountOwnedBy( state.Players[idx].Id ) > 0 )
			{
				if ( idx <= previous )
					state.TurnNumber++; // wrapped around to a new round
				state.CurrentPlayerIndex = idx;
				return;
			}
		}
	}

	private void CheckVictory()
	{
		var winner = VictoryChecker.CheckWinner( _ctx.State );
		if ( winner != null && !_ctx.State.IsOver )
		{
			_ctx.State.WinnerPlayerId = winner;
			_ctx.Events.Raise( new GameWon( winner ) );
		}
	}
}
