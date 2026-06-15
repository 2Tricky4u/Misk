namespace Misk.Domain;

/// <summary>
/// Everything a command/phase needs to read and mutate a match: the state plus the
/// injected services. All mutations go through the helper methods here so that the
/// matching event is always raised — this is the single choke point that keeps the
/// network mirror and UI in sync with the domain.
/// </summary>
public sealed class GameContext
{
	public GameState State { get; }
	public IDiceRoller Dice { get; }
	public ICombatResolver Combat { get; }
	public IReinforcementCalculator Reinforcements { get; }
	public GameEventBus Events { get; }

	public RulesConfig Rules => State.Rules;
	public GameMap Map => State.Map;

	public GameContext( GameState state, IDiceRoller dice, ICombatResolver combat,
		IReinforcementCalculator reinforcements, GameEventBus events )
	{
		State = state;
		Dice = dice;
		Combat = combat;
		Reinforcements = reinforcements;
		Events = events;
	}

	public void SetArmies( Territory territory, int armies )
	{
		territory.Armies = armies;
		Events.Raise( new ArmiesChanged( territory.Id, armies ) );
	}

	public void AddArmies( Territory territory, int delta ) => SetArmies( territory, territory.Armies + delta );

	public void SetOwner( Territory territory, string newOwnerPlayerId )
	{
		var previous = territory.OwnerPlayerId;
		territory.OwnerPlayerId = newOwnerPlayerId;
		Events.Raise( new OwnerChanged( territory.Id, newOwnerPlayerId, previous ) );
	}

	public void SetPhase( GamePhase phase )
	{
		State.Phase = phase;
		Events.Raise( new PhaseChanged( phase ) );
	}

	public void SetPendingReinforcements( int amount )
	{
		State.PendingReinforcements = amount;
		Events.Raise( new ReinforcementsChanged( State.CurrentPlayer.Id, amount ) );
	}

	// ---- cards ----
	public Card DrawCard( string playerId )
	{
		var card = State.Deck?.Draw();
		if ( card == null )
			return null;

		State.Hand( playerId ).Add( card );
		Events.Raise( new CardDrawn( playerId ) );
		Events.Raise( new HandChanged( playerId ) );
		return card;
	}

	public void AddToHand( string playerId, System.Collections.Generic.IEnumerable<Card> cards )
	{
		State.Hand( playerId ).AddRange( cards );
		Events.Raise( new HandChanged( playerId ) );
	}

	public void RemoveCards( string playerId, System.Collections.Generic.IReadOnlyList<Card> cards )
	{
		var hand = State.Hand( playerId );
		foreach ( var card in cards )
			hand.Remove( card );
		State.Deck?.Discard( cards );
		Events.Raise( new HandChanged( playerId ) );
	}

	// ---- advance after capture ----
	public void SetPendingAdvance( AdvanceRequest? request )
	{
		State.PendingAdvance = request;
		Events.Raise( new AdvanceChanged() );
	}

	// ---- setup ----
	public void RaiseSetupChanged() => Events.Raise( new SetupChanged() );
}
