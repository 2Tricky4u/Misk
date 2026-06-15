namespace Misk.Domain;

/// <summary>Place reinforcement armies into one owned territory during the reinforce phase.</summary>
public sealed class DeployCommand : IGameCommand
{
	public string PlayerId { get; }
	public string TerritoryId { get; }
	public int Count { get; }

	public DeployCommand( string playerId, string territoryId, int count )
	{
		PlayerId = playerId;
		TerritoryId = territoryId;
		Count = count;
	}

	public CommandResult Validate( GameContext ctx )
	{
		if ( ctx.State.CurrentPlayer.Id != PlayerId )
			return CommandResult.Fail( "It is not your turn." );

		if ( Count <= 0 )
			return CommandResult.Fail( "Must deploy at least one army." );

		if ( Count > ctx.State.PendingReinforcements )
			return CommandResult.Fail( "Not enough reinforcements remaining." );

		var territory = ctx.Map.Territory( TerritoryId );
		if ( territory == null )
			return CommandResult.Fail( "Unknown territory." );

		if ( territory.OwnerPlayerId != PlayerId )
			return CommandResult.Fail( "You can only reinforce your own territories." );

		return CommandResult.Success;
	}

	public void Execute( GameContext ctx )
	{
		var territory = ctx.Map.Territory( TerritoryId );
		ctx.AddArmies( territory, Count );
		ctx.SetPendingReinforcements( ctx.State.PendingReinforcements - Count );
	}
}
