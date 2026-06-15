using System.Collections.Generic;
using System.Linq;

namespace Misk.Domain;

/// <summary>
/// Trade a set of three RISK cards during the reinforce phase for armies (escalating with the
/// number of sets traded in the game). If a traded card depicts a territory the player owns,
/// they also get bonus armies placed directly on it.
/// </summary>
public sealed class TradeCardsCommand : IGameCommand
{
	public string PlayerId { get; }
	public IReadOnlyList<string> CardIds { get; }

	public TradeCardsCommand( string playerId, IReadOnlyList<string> cardIds )
	{
		PlayerId = playerId;
		CardIds = cardIds;
	}

	public CommandResult Validate( GameContext ctx )
	{
		if ( ctx.State.CurrentPlayer.Id != PlayerId )
			return CommandResult.Fail( "It is not your turn." );

		if ( !ctx.Rules.CardsEnabled )
			return CommandResult.Fail( "Cards are disabled." );

		if ( CardIds == null || CardIds.Count != 3 )
			return CommandResult.Fail( "A set is exactly three cards." );

		var cards = ResolveCards( ctx );
		if ( cards == null )
			return CommandResult.Fail( "You do not hold those cards." );

		if ( !CardSetEvaluator.IsValidSet( cards[0], cards[1], cards[2] ) )
			return CommandResult.Fail( "Those three cards are not a valid set." );

		return CommandResult.Success;
	}

	public void Execute( GameContext ctx )
	{
		var cards = ResolveCards( ctx );

		int value = CardSetEvaluator.SetValue( ctx.State.CardSetsTradedIn, ctx.Rules );
		ctx.State.CardSetsTradedIn++;
		ctx.SetPendingReinforcements( ctx.State.PendingReinforcements + value );

		// Bonus armies for a depicted territory you own (placed there; capped once per trade).
		int bonus = 0;
		foreach ( var card in cards )
		{
			if ( card.TerritoryId == null )
				continue;
			var territory = ctx.Map.Territory( card.TerritoryId );
			if ( territory != null && territory.OwnerPlayerId == PlayerId )
			{
				ctx.AddArmies( territory, ctx.Rules.CardTerritoryBonus );
				bonus = ctx.Rules.CardTerritoryBonus;
				break;
			}
		}

		ctx.RemoveCards( PlayerId, cards );
		ctx.Events.Raise( new CardsTraded( PlayerId, value, bonus ) );
	}

	private List<Card> ResolveCards( GameContext ctx )
	{
		var hand = ctx.State.Hand( PlayerId );
		var cards = new List<Card>( 3 );
		foreach ( var id in CardIds )
		{
			var card = hand.FirstOrDefault( c => c.Id == id );
			if ( card == null || cards.Contains( card ) )
				return null;
			cards.Add( card );
		}
		return cards;
	}
}
