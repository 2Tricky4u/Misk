using System;

namespace Misk.Domain;

/// <summary>
/// Lightweight typed pub/sub that decouples state mutation from presentation/networking.
/// The domain raises events; the host replicates them and the UI redraws — neither knows
/// about the other. <see cref="Changed"/> is a catch-all "something changed" signal handy
/// for cheap UI refreshes.
/// </summary>
public sealed class GameEventBus
{
	public event Action<ArmiesChanged> ArmiesChanged;
	public event Action<OwnerChanged> OwnerChanged;
	public event Action<PhaseChanged> PhaseChanged;
	public event Action<TurnChanged> TurnChanged;
	public event Action<ReinforcementsChanged> ReinforcementsChanged;
	public event Action<CombatResolved> CombatResolved;
	public event Action<GameWon> GameWon;
	public event Action<HandChanged> HandChanged;
	public event Action<CardDrawn> CardDrawn;
	public event Action<CardsTraded> CardsTraded;
	public event Action<AdvanceChanged> AdvanceChanged;
	public event Action<SetupChanged> SetupChanged;

	/// <summary>Fires after every other event — subscribe here to refresh the whole view cheaply.</summary>
	public event Action Changed;

	public void Raise( ArmiesChanged e ) { ArmiesChanged?.Invoke( e ); Changed?.Invoke(); }
	public void Raise( OwnerChanged e ) { OwnerChanged?.Invoke( e ); Changed?.Invoke(); }
	public void Raise( PhaseChanged e ) { PhaseChanged?.Invoke( e ); Changed?.Invoke(); }
	public void Raise( TurnChanged e ) { TurnChanged?.Invoke( e ); Changed?.Invoke(); }
	public void Raise( ReinforcementsChanged e ) { ReinforcementsChanged?.Invoke( e ); Changed?.Invoke(); }
	public void Raise( CombatResolved e ) { CombatResolved?.Invoke( e ); Changed?.Invoke(); }
	public void Raise( GameWon e ) { GameWon?.Invoke( e ); Changed?.Invoke(); }
	public void Raise( HandChanged e ) { HandChanged?.Invoke( e ); Changed?.Invoke(); }
	public void Raise( CardDrawn e ) { CardDrawn?.Invoke( e ); Changed?.Invoke(); }
	public void Raise( CardsTraded e ) { CardsTraded?.Invoke( e ); Changed?.Invoke(); }
	public void Raise( AdvanceChanged e ) { AdvanceChanged?.Invoke( e ); Changed?.Invoke(); }
	public void Raise( SetupChanged e ) { SetupChanged?.Invoke( e ); Changed?.Invoke(); }
}
