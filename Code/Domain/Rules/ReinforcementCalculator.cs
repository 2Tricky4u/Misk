using System.Linq;

namespace Misk.Domain;

/// <summary>Strategy for computing how many reinforcements a player receives. Swap for rule variants.</summary>
public interface IReinforcementCalculator
{
	int Calculate( GameState state, string playerId );
}

/// <summary>
/// Classic Risk reinforcement: max(min, floor(territories / perReinforcement)) plus the
/// bonus of every fully-owned region.
/// </summary>
public sealed class StandardReinforcementCalculator : IReinforcementCalculator
{
	public int Calculate( GameState state, string playerId )
	{
		var rules = state.Rules;
		int owned = state.Map.CountOwnedBy( playerId );

		int baseReinforcements = System.Math.Max(
			rules.MinReinforcements,
			owned / rules.TerritoriesPerReinforcement );

		int regionBonus = state.Map.Regions
			.Where( r => state.Map.RegionFullyOwnedBy( r.Id, playerId ) )
			.Sum( r => r.Bonus );

		return baseReinforcements + regionBonus;
	}
}
