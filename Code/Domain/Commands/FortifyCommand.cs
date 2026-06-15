namespace Misk.Domain;

/// <summary>
/// Move armies between two adjacent friendly territories during the fortify phase. Limited
/// to RulesConfig.FortifyMovesPerTurn moves, always leaving MinArmiesLeftBehind in the source.
/// (Adjacency-only for now; path-based fortify is a future variant.)
/// </summary>
public sealed class FortifyCommand : IGameCommand
{
	public string PlayerId { get; }
	public string FromTerritoryId { get; }
	public string ToTerritoryId { get; }
	public int Count { get; }

	public FortifyCommand( string playerId, string fromTerritoryId, string toTerritoryId, int count )
	{
		PlayerId = playerId;
		FromTerritoryId = fromTerritoryId;
		ToTerritoryId = toTerritoryId;
		Count = count;
	}

	public CommandResult Validate( GameContext ctx )
	{
		if ( ctx.State.CurrentPlayer.Id != PlayerId )
			return CommandResult.Fail( "It is not your turn." );

		if ( ctx.State.FortifyMovesUsed >= ctx.Rules.FortifyMovesPerTurn )
			return CommandResult.Fail( "No fortify moves left this turn." );

		if ( Count <= 0 )
			return CommandResult.Fail( "Must move at least one army." );

		var from = ctx.Map.Territory( FromTerritoryId );
		var to = ctx.Map.Territory( ToTerritoryId );

		if ( from == null || to == null )
			return CommandResult.Fail( "Unknown territory." );

		if ( from.OwnerPlayerId != PlayerId || to.OwnerPlayerId != PlayerId )
			return CommandResult.Fail( "Both territories must be yours." );

		if ( !from.IsAdjacentTo( ToTerritoryId ) )
			return CommandResult.Fail( "Territories are not adjacent." );

		if ( from.Armies - Count < ctx.Rules.MinArmiesLeftBehind )
			return CommandResult.Fail( $"Must leave at least {ctx.Rules.MinArmiesLeftBehind} army behind." );

		return CommandResult.Success;
	}

	public void Execute( GameContext ctx )
	{
		var from = ctx.Map.Territory( FromTerritoryId );
		var to = ctx.Map.Territory( ToTerritoryId );

		ctx.AddArmies( from, -Count );
		ctx.AddArmies( to, Count );
		ctx.State.FortifyMovesUsed++;
	}
}
