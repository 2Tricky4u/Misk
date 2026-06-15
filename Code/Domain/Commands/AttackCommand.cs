using System;
using System.Linq;

namespace Misk.Domain;

/// <summary>
/// One Risk-style dice exchange from an owned territory into an adjacent enemy one. The
/// attacker chooses how many dice to roll (1..min(rulesMax, armies-1)); the defender
/// auto-rolls the most legal dice. On a capture the territory is taken and the attacker is
/// asked how many armies to advance (see <see cref="AdvanceArmiesCommand"/>), unless only one
/// amount is legal. Captures also flag a card draw and steal an eliminated player's cards.
/// </summary>
public sealed class AttackCommand : IGameCommand
{
	public string PlayerId { get; }
	public string FromTerritoryId { get; }
	public string ToTerritoryId { get; }
	public int AttackerDice { get; }

	public AttackCommand( string playerId, string fromTerritoryId, string toTerritoryId, int attackerDice )
	{
		PlayerId = playerId;
		FromTerritoryId = fromTerritoryId;
		ToTerritoryId = toTerritoryId;
		AttackerDice = attackerDice;
	}

	public CommandResult Validate( GameContext ctx )
	{
		if ( ctx.State.CurrentPlayer.Id != PlayerId )
			return CommandResult.Fail( "It is not your turn." );

		var from = ctx.Map.Territory( FromTerritoryId );
		var to = ctx.Map.Territory( ToTerritoryId );

		if ( from == null || to == null )
			return CommandResult.Fail( "Unknown territory." );

		if ( from.OwnerPlayerId != PlayerId )
			return CommandResult.Fail( "You can only attack from your own territory." );

		if ( to.OwnerPlayerId == PlayerId )
			return CommandResult.Fail( "You cannot attack your own territory." );

		if ( !from.IsAdjacentTo( ToTerritoryId ) )
			return CommandResult.Fail( "Territories are not adjacent." );

		if ( from.Armies < ctx.Rules.MinArmiesToAttack )
			return CommandResult.Fail( $"Need at least {ctx.Rules.MinArmiesToAttack} armies to attack." );

		if ( AttackerDice < 1 )
			return CommandResult.Fail( "Must roll at least one die." );

		return CommandResult.Success;
	}

	public void Execute( GameContext ctx )
	{
		var from = ctx.Map.Territory( FromTerritoryId );
		var to = ctx.Map.Territory( ToTerritoryId );

		string defenderPlayerId = to.OwnerPlayerId;
		var outcome = ctx.Combat.Resolve( from.Armies, to.Armies, AttackerDice, ctx.Rules, ctx.Dice );

		ctx.AddArmies( from, -outcome.AttackerLosses );
		ctx.AddArmies( to, -outcome.DefenderLosses );

		bool captured = false;
		if ( to.Armies <= 0 )
		{
			captured = true;
			ctx.SetOwner( to, PlayerId );
			ctx.State.CurrentPlayerCapturedThisTurn = true;

			// Defeating a player's last territory: take their cards (rulebook).
			if ( defenderPlayerId != null && ctx.Map.CountOwnedBy( defenderPlayerId ) == 0 )
			{
				var loot = ctx.State.Hand( defenderPlayerId );
				if ( loot.Count > 0 )
				{
					var taken = loot.ToList();
					loot.Clear();
					ctx.Events.Raise( new HandChanged( defenderPlayerId ) );
					ctx.AddToHand( PlayerId, taken );
				}
			}

			// Must move in at least the dice rolled, up to all-but-one.
			int diceRolled = outcome.AttackerDice.Count;
			int max = from.Armies - 1;
			int min = Math.Max( 1, Math.Min( diceRolled, max ) );

			if ( min >= max )
			{
				ctx.AddArmies( from, -max );
				ctx.SetArmies( to, max );
			}
			else
			{
				ctx.SetArmies( to, 0 );
				ctx.SetPendingAdvance( new AdvanceRequest
				{
					FromId = FromTerritoryId,
					ToId = ToTerritoryId,
					Min = min,
					Max = max
				} );
			}
		}

		ctx.Events.Raise( new CombatResolved
		{
			FromTerritoryId = FromTerritoryId,
			ToTerritoryId = ToTerritoryId,
			AttackerPlayerId = PlayerId,
			DefenderPlayerId = defenderPlayerId,
			AttackerDice = outcome.AttackerDice,
			DefenderDice = outcome.DefenderDice,
			AttackerLosses = outcome.AttackerLosses,
			DefenderLosses = outcome.DefenderLosses,
			Captured = captured
		} );
	}
}
