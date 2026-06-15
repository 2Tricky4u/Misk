using System;
using Sandbox;

namespace Misk.Presentation;

/// <summary>
/// Client-side audio. Plays 2D UI sound events by key — each key maps by convention to a
/// resource at <c>sounds/&lt;key&gt;.sound</c> (a SoundEvent wrapping the generated clip) — and
/// keeps a looping music bed alive. Every call is guarded so a missing/unimported sound (e.g.
/// before the editor has compiled the assets) never disrupts play. Per-sound volume lives in the
/// .sound resources, so SFX are fire-and-forget.
/// </summary>
public static class MiskAudio
{
	private static SoundHandle _music;

	private static string PathFor( string key ) => $"sounds/{key}.sound";

	/// <summary>Fire a one-shot SFX (no-op if the sound isn't present).</summary>
	public static void Play( string key )
	{
		try
		{
			Sound.Play( PathFor( key ) );
		}
		catch ( Exception e )
		{
			Log.Info( $"[Misk] sound '{key}' unavailable: {e.Message}" );
		}
	}

	/// <summary>Start the music bed if it isn't already playing; restarts it when it finishes (loop).</summary>
	public static void EnsureMusic( string key )
	{
		if ( _music.IsValid() )
			return;
		try
		{
			_music = Sound.Play( PathFor( key ) );
		}
		catch ( Exception e )
		{
			Log.Info( $"[Misk] music '{key}' unavailable: {e.Message}" );
		}
	}

	public static void StopMusic()
	{
		try
		{
			if ( _music.IsValid() )
				_music.Stop( 0.7f );
		}
		catch { }
	}
}
