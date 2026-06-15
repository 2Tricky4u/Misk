namespace Misk.Domain;

/// <summary>Win condition: a single faction controls every territory.</summary>
public static class VictoryChecker
{
	/// <summary>Returns the winning player's id, or null if the game continues.</summary>
	public static string CheckWinner( GameState state )
	{
		string first = null;
		foreach ( var territory in state.Map.Territories )
		{
			if ( territory.OwnerPlayerId == null )
				return null;

			if ( first == null )
				first = territory.OwnerPlayerId;
			else if ( territory.OwnerPlayerId != first )
				return null;
		}
		return first;
	}
}
