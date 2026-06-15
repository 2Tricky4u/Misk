using System;

namespace Misk.Domain;

/// <summary>
/// Source of randomness for combat and setup. Injected (and seedable) so games are
/// deterministic and the domain stays testable without the engine's RNG.
/// </summary>
public interface IDiceRoller
{
	/// <summary>A single six-sided die, 1..6.</summary>
	int Roll();

	/// <summary>A random integer in [0, maxExclusive). Used for shuffles/placement.</summary>
	int Next( int maxExclusive );
}

public sealed class SystemDiceRoller : IDiceRoller
{
	private readonly Random _random;

	public SystemDiceRoller( int? seed = null )
	{
		_random = seed.HasValue ? new Random( seed.Value ) : new Random();
	}

	public int Roll() => _random.Next( 1, 7 );

	public int Next( int maxExclusive ) => _random.Next( maxExclusive );
}
