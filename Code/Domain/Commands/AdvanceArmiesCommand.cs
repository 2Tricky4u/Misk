namespace Misk.Domain;

/// <summary>
/// Resolves a pending capture by advancing the chosen number of armies into the newly taken
/// territory (between the dice rolled and all-but-one). Only valid while a capture is pending.
/// </summary>
public sealed class AdvanceArmiesCommand : IGameCommand
{
	public string PlayerId { get; }
	public int Count { get; }

	public AdvanceArmiesCommand( string playerId, int count )
	{
		PlayerId = playerId;
		Count = count;
	}

	public CommandResult Validate( GameContext ctx )
	{
		if ( ctx.State.CurrentPlayer.Id != PlayerId )
			return CommandResult.Fail( "It is not your turn." );

		if ( ctx.State.PendingAdvance is not { } advance )
			return CommandResult.Fail( "There is no capture to resolve." );

		if ( Count < advance.Min || Count > advance.Max )
			return CommandResult.Fail( $"Must advance between {advance.Min} and {advance.Max} armies." );

		return CommandResult.Success;
	}

	public void Execute( GameContext ctx )
	{
		var advance = ctx.State.PendingAdvance.Value;
		var from = ctx.Map.Territory( advance.FromId );
		var to = ctx.Map.Territory( advance.ToId );

		ctx.AddArmies( from, -Count );
		ctx.SetArmies( to, Count );
		ctx.SetPendingAdvance( null );
	}
}
