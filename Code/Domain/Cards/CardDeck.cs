using System.Collections.Generic;

namespace Misk.Domain;

/// <summary>
/// The draw/discard pile of RISK cards. Built from the map (one troop card per territory,
/// kinds assigned cyclically, plus a number of wild Banners) and shuffled with the injected,
/// seedable RNG so games stay deterministic. Host-only — clients never see the deck.
/// </summary>
public sealed class CardDeck
{
	private readonly List<Card> _draw = new();
	private readonly List<Card> _discard = new();
	private readonly IDiceRoller _rng;

	public int Count => _draw.Count + _discard.Count;

	public CardDeck( GameMap map, RulesConfig rules, IDiceRoller rng )
	{
		_rng = rng;

		int kindIndex = 0;
		foreach ( var territory in map.Territories )
		{
			var kind = (CardKind)(kindIndex % 3); // Footmen, Riders, Siege
			kindIndex++;
			_draw.Add( new Card( $"card_{territory.Id}", kind, territory.Id ) );
		}

		for ( int i = 0; i < rules.WildCardCount; i++ )
			_draw.Add( new Card( $"card_banner_{i}", CardKind.Banner, null ) );

		Shuffle( _draw );
	}

	/// <summary>Draw the top card, reshuffling the discard back in if the draw pile is empty.</summary>
	public Card Draw()
	{
		if ( _draw.Count == 0 )
		{
			if ( _discard.Count == 0 )
				return null;
			_draw.AddRange( _discard );
			_discard.Clear();
			Shuffle( _draw );
		}

		var top = _draw[_draw.Count - 1];
		_draw.RemoveAt( _draw.Count - 1 );
		return top;
	}

	/// <summary>Return traded/spent cards to the discard pile.</summary>
	public void Discard( IEnumerable<Card> cards ) => _discard.AddRange( cards );

	private void Shuffle( IList<Card> list )
	{
		for ( int i = list.Count - 1; i > 0; i-- )
		{
			int j = _rng.Next( i + 1 );
			(list[i], list[j]) = (list[j], list[i]);
		}
	}
}
