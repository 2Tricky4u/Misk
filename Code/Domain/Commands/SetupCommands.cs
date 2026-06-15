namespace Misk.Domain;

/// <summary>
/// Claim an unowned territory during the Claim step of manual setup: places one army and
/// marks ownership. (The SetupController advances the placer and switches to the Place step.)
/// </summary>
public sealed class ClaimCommand : IGameCommand
{
	public string PlayerId { get; }
	public string TerritoryId { get; }

	public ClaimCommand( string playerId, string territoryId )
	{
		PlayerId = playerId;
		TerritoryId = territoryId;
	}

	public CommandResult Validate( GameContext ctx )
	{
		if ( !ctx.State.InSetup || ctx.State.SetupStep != SetupStep.Claim )
			return CommandResult.Fail( "Not in the claim step." );

		if ( ctx.State.CurrentPlayer.Id != PlayerId )
			return CommandResult.Fail( "It is not your placement." );

		if ( SetupArmiesLeft( ctx ) <= 0 )
			return CommandResult.Fail( "You have no armies left to place." );

		var territory = ctx.Map.Territory( TerritoryId );
		if ( territory == null )
			return CommandResult.Fail( "Unknown territory." );

		if ( territory.OwnerPlayerId != null )
			return CommandResult.Fail( "That territory is already claimed." );

		return CommandResult.Success;
	}

	public void Execute( GameContext ctx )
	{
		var territory = ctx.Map.Territory( TerritoryId );
		ctx.SetOwner( territory, PlayerId );
		ctx.SetArmies( territory, 1 );
		ctx.State.SetupArmiesRemaining[PlayerId] = SetupArmiesLeft( ctx ) - 1;
	}

	private int SetupArmiesLeft( GameContext ctx )
		=> ctx.State.SetupArmiesRemaining.TryGetValue( PlayerId, out var n ) ? n : 0;
}

/// <summary>
/// Place one starting army on a territory you already own, during the Place step of manual setup.
/// </summary>
public sealed class PlaceArmyCommand : IGameCommand
{
	public string PlayerId { get; }
	public string TerritoryId { get; }

	public PlaceArmyCommand( string playerId, string territoryId )
	{
		PlayerId = playerId;
		TerritoryId = territoryId;
	}

	public CommandResult Validate( GameContext ctx )
	{
		if ( !ctx.State.InSetup || ctx.State.SetupStep != SetupStep.Place )
			return CommandResult.Fail( "Not in the muster step." );

		if ( ctx.State.CurrentPlayer.Id != PlayerId )
			return CommandResult.Fail( "It is not your placement." );

		if ( SetupArmiesLeft( ctx ) <= 0 )
			return CommandResult.Fail( "You have no armies left to place." );

		var territory = ctx.Map.Territory( TerritoryId );
		if ( territory == null )
			return CommandResult.Fail( "Unknown territory." );

		if ( territory.OwnerPlayerId != PlayerId )
			return CommandResult.Fail( "You can only reinforce your own territory." );

		return CommandResult.Success;
	}

	public void Execute( GameContext ctx )
	{
		var territory = ctx.Map.Territory( TerritoryId );
		ctx.AddArmies( territory, 1 );
		ctx.State.SetupArmiesRemaining[PlayerId] = SetupArmiesLeft( ctx ) - 1;
	}

	private int SetupArmiesLeft( GameContext ctx )
		=> ctx.State.SetupArmiesRemaining.TryGetValue( PlayerId, out var n ) ? n : 0;
}
