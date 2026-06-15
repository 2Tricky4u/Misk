using System.Collections.Generic;
using System.Linq;

namespace Misk.Domain;

/// <summary>
/// Seeds the opening board: distributes territories fairly (shuffled round-robin), gives
/// each one army, then scatters the rest of each player's starting armies across the
/// territories they own. Deterministic for a given RNG seed. Mutates territories directly;
/// the host replicates the resulting board once before play begins.
/// </summary>
public static class GameSetup
{
	public static void AssignInitialState( GameState state, IDiceRoller rng )
	{
		var players = state.Players;
		if ( players.Count == 0 )
			return;

		var territories = state.Map.Territories.ToList();
		Shuffle( territories, rng );

		// Round-robin ownership, one army each.
		for ( int i = 0; i < territories.Count; i++ )
		{
			var owner = players[i % players.Count];
			territories[i].OwnerPlayerId = owner.Id;
			territories[i].Armies = 1;
		}

		// Scatter each player's remaining starting armies onto their own territories.
		int startingArmies = state.Rules.StartingArmiesFor( players.Count );
		foreach ( var player in players )
		{
			var owned = state.Map.OwnedBy( player.Id ).ToList();
			if ( owned.Count == 0 )
				continue;

			int remaining = startingArmies - owned.Count;
			for ( int k = 0; k < remaining; k++ )
			{
				var territory = owned[rng.Next( owned.Count )];
				territory.Armies++;
			}
		}
	}

	private static void Shuffle<T>( IList<T> list, IDiceRoller rng )
	{
		for ( int i = list.Count - 1; i > 0; i-- )
		{
			int j = rng.Next( i + 1 );
			(list[i], list[j]) = (list[j], list[i]);
		}
	}
}
