using System.Linq;

namespace Misk.Domain;

/// <summary>
/// Drives the manual draft (claim territories one-by-one, then place remaining starting
/// armies), mirroring TurnController's shape: validate a command, run it, then advance the
/// placer and transition steps. When every army is placed it hands off to TurnController.
/// </summary>
public sealed class SetupController
{
	private readonly GameContext _ctx;
	private readonly TurnController _turnController;

	public SetupController( GameContext ctx, TurnController turnController )
	{
		_ctx = ctx;
		_turnController = turnController;
	}

	public void Begin()
	{
		var state = _ctx.State;
		state.InSetup = true;
		state.SetupStep = SetupStep.Claim;
		state.CurrentPlayerIndex = 0;

		int starting = state.Rules.StartingArmiesFor( state.Players.Count );
		foreach ( var player in state.Players )
			state.SetupArmiesRemaining[player.Id] = starting;

		_ctx.RaiseSetupChanged();
	}

	public CommandResult Execute( IGameCommand command )
	{
		var state = _ctx.State;
		if ( !state.InSetup )
			return CommandResult.Fail( "Setup is complete." );

		bool accepted =
			(state.SetupStep == SetupStep.Claim && command is ClaimCommand) ||
			(state.SetupStep == SetupStep.Place && command is PlaceArmyCommand);
		if ( !accepted )
			return CommandResult.Fail( "That is not a valid setup action right now." );

		var validation = command.Validate( _ctx );
		if ( !validation.Ok )
			return validation;

		command.Execute( _ctx );
		Advance();
		return CommandResult.Success;
	}

	private void Advance()
	{
		var state = _ctx.State;

		// Once every territory is owned, the rest of the armies are placed on owned land.
		if ( state.SetupStep == SetupStep.Claim && state.Map.Territories.All( t => t.OwnerPlayerId != null ) )
			state.SetupStep = SetupStep.Place;

		if ( state.SetupArmiesRemaining.Values.Sum() <= 0 )
		{
			Finish();
			return;
		}

		AdvanceToNextPlacer();
		_ctx.RaiseSetupChanged();
	}

	private void AdvanceToNextPlacer()
	{
		var state = _ctx.State;
		int count = state.Players.Count;
		int previous = state.CurrentPlayerIndex;

		for ( int step = 0; step < count; step++ )
		{
			int idx = (previous + 1 + step) % count;
			if ( state.SetupArmiesRemaining.TryGetValue( state.Players[idx].Id, out var n ) && n > 0 )
			{
				state.CurrentPlayerIndex = idx;
				return;
			}
		}
	}

	private void Finish()
	{
		_ctx.State.InSetup = false;
		_ctx.RaiseSetupChanged();
		_turnController.StartGame();
	}
}
