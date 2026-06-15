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
		"draw" => 0.65f,
		"trade" => 0.85f,
		"victory" => 0.95f,
		"defeat" => 0.85f,
		_ => 0.8f,
	};

	private static string PathFor( string key ) => $"sounds/{key}.sound";

	/// <summary>Fire a one-shot SFX (no-op if the sound isn't present).</summary>
	public static void Play( string key )
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
