using System;
using Sandbox;

namespace Misk.Presentation;

/// <summary>
/// Client-side audio. Plays 2D UI sound events by key — each key maps by convention to a
/// resource at <c>sounds/&lt;key&gt;.sound</c> (a SoundEvent wrapping the generated clip) — and
/// keeps a looping music bed alive, switching tracks by game mode. Per-sound balance lives here
/// (BaseVol) and is multiplied by the player's master SFX/Music volume from Settings. Every call
/// is guarded so a missing/unimported sound never disrupts play.
/// </summary>
public static class MiskAudio
{
	private static SoundHandle _music;
	private static string _musicKey;

	// Deferred card-cue scheduling. Card sounds (shuffle/draw) hold back just briefly so they don't
	// slam on the exact instant of a battle hit, then play. Driven by Tick(). A card cue waits at
	// most MaxEffectWait behind effects (super small), but always fully behind another card cue so
	// the draw and the shuffle never overlap each other.
	private const float MaxEffectWait = 0.3f;
	private static float _sfxBusyUntil;    // battle / other effects occupy the channel until here
	private static float _cardBusyUntil;   // a playing card cue occupies the channel until here
	private struct Cue { public string Key; public float EnqueuedAt; }
	private static readonly Queue<Cue> _cardQueue = new();

	/// <summary>Master volumes (0..1), set from the Settings panel.</summary>
	public static float MusicVolume { get; private set; } = 0.5f;
	public static float SfxVolume { get; private set; } = 0.85f;

	// Per-clip balance, before the master multiplier.
	private static float BaseVol( string key ) => key switch
	{
		"clash" => 0.9f,
		"horn" => 0.85f,
		"march" => 0.6f,
		"deploy" => 0.75f,
		"dice" => 0.6f,
		"turn" => 0.7f,
		"phase" => 0.75f,
		"click" => 0.5f,
		"draw" => 1.0f,
		"shuffle" => 1.0f,
		"trade" => 0.85f,
		"victory" => 0.95f,
		"defeat" => 0.85f,
		_ => 0.8f,
	};

	private static string PathFor( string key ) => $"sounds/{key}.sound";

	// Real clip lengths (seconds), used to reserve the SFX channel so a queued card cue waits until
	// the battle/other effect that is playing has finished, and so each card cue itself plays in full.
	private static float Duration( string key ) => key switch
	{
		"clash" => 1.54f,
		"horn" => 2.53f,
		"dice" => 1.07f,
		"deploy" => 1.54f,
		"march" => 1.25f,
		"turn" => 1.65f,
		"phase" => 1.33f,
		"click" => 0.55f,
		"draw" => 0.79f,
		"shuffle" => 1.08f,
		"trade" => 1.65f,
		"victory" => 5.04f,
		"defeat" => 2.53f,
		_ => 0.8f,
	};

	// The card cues are the only deferred sounds — they politely wait their turn.
	private static bool IsCardSound( string key ) => key == "draw" || key == "shuffle";

	/// <summary>Fire a one-shot SFX (no-op if the sound isn't present). Card cues (shuffle/draw) are
	/// queued instead of played immediately — Tick() releases them once the channel is free.</summary>
	public static void Play( string key )
	{
		if ( IsCardSound( key ) )
		{
			// Collapse duplicates so rapid tray opens don't stack a pile of riffles.
			foreach ( var c in _cardQueue )
				if ( c.Key == key )
					return;
			_cardQueue.Enqueue( new Cue { Key = key, EnqueuedAt = Time.Now } );
			return;
		}

		PlayNow( key );
		// Battle and other effects hold the channel for their full length; a card cue only honours
		// this up to MaxEffectWait (see Tick), so it never feels like a long pause.
		_sfxBusyUntil = Math.Max( _sfxBusyUntil, Time.Now + Duration( key ) );
	}

	/// <summary>Play a sound right now, bypassing the queue.</summary>
	private static void PlayNow( string key )
	{
		try
		{
			var h = Sound.Play( PathFor( key ) );
			h.Volume = BaseVol( key ) * SfxVolume;
		}
		catch ( Exception e )
		{
			Log.Info( $"[Misk] sound '{key}' unavailable: {e.Message}" );
		}
	}

	/// <summary>Drain the deferred card-cue queue. Call once per frame (from MiskGame.OnUpdate): a
	/// queued shuffle/draw only fires once the priority channel is free, then reserves the channel for
	/// its own full length so it is never cut off and two cues never overlap.</summary>
	public static void Tick()
	{
		if ( _cardQueue.Count == 0 )
			return;

		float now = Time.Now;
		// Statics survive a Play restart but scene time resets — clamp any implausible leftover
		// reservation (longer than the longest clip) so the queue can never stall.
		if ( _sfxBusyUntil - now > 6f )
			_sfxBusyUntil = now;
		if ( _cardBusyUntil - now > 6f )
			_cardBusyUntil = now;

		var head = _cardQueue.Peek();
		// Wait fully behind a prior card cue (no overlap), but only briefly behind battle/other
		// effects — at most MaxEffectWait after this cue was raised.
		float effectGate = Math.Min( _sfxBusyUntil, head.EnqueuedAt + MaxEffectWait );
		if ( now < _cardBusyUntil || now < effectGate )
			return;

		_cardQueue.Dequeue();
		PlayNow( head.Key );
		_cardBusyUntil = now + Duration( head.Key );
	}

	/// <summary>Soft UI click feedback for buttons.</summary>
	public static void Click() => Play( "click" );

	/// <summary>Keep the given music bed playing, switching tracks (and looping) as needed.</summary>
	public static void EnsureMusic( string key )
	{
		if ( _musicKey != key )
		{
			try { if ( _music.IsValid() ) _music.Stop( 0.6f ); } catch { }
			_music = default;
			_musicKey = key;
		}
		if ( _music.IsValid() )
		{
			_music.Volume = MusicVolume;
			return;
		}
		try
		{
			_music = Sound.Play( PathFor( key ) );
			_music.Volume = MusicVolume;
		}
		catch ( Exception e )
		{
			Log.Info( $"[Misk] music '{key}' unavailable: {e.Message}" );
		}
	}

	public static void StopMusic()
	{
		try { if ( _music.IsValid() ) _music.Stop( 0.7f ); } catch { }
		_music = default;
		_musicKey = null;
	}

	public static void SetMusicVolume( float v )
	{
		MusicVolume = Math.Clamp( v, 0f, 1f );
		try { if ( _music.IsValid() ) _music.Volume = MusicVolume; } catch { }
	}

	public static void SetSfxVolume( float v ) => SfxVolume = Math.Clamp( v, 0f, 1f );
}
