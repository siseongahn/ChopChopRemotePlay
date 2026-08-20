using UnityEngine;

/// <summary>
/// Paces frames by a timer rather than by the local display while somebody is watching remotely.
/// </summary>
/// <remarks>
/// With vsync on, a frame ends by waiting for the monitor's next blanking interval. That is the right thing
/// when somebody is sitting in front of the monitor, and the wrong thing when the picture is going down a
/// wire instead - the viewer's frame rate has nothing to do with this machine's refresh.
///
/// It also turns out to be where the slow motion comes from. With the screen dark, frame delivery measured
/// 78.5ms while the engine went on reporting 16.65ms per frame, which is one refresh interval to the
/// hundredth of a millisecond. The simulation ran at about a fifth of real time, smoothly, because every
/// frame was handed the same step. Audio kept perfect time throughout, so nothing was suspended - the
/// clock the game moves by was simply tied to a display that had stopped presenting.
///
/// Turning the local monitor off by hand is not something a power request can prevent, unlike Windows
/// idling the display out, so this is the half of the problem that can be fixed from inside the game.
///
/// The previous values are put back on disconnect rather than assumed, so a project that ships with vsync
/// off, or a frame rate set for some other reason, gets its own settings returned.
/// </remarks>
public static class RemotePlayFramePacing
{
	//What the viewer is served at. Matching the usual refresh keeps motion looking as it did.
    private const int StreamingFrameRate = 60;

	private static bool s_Held;
	private static int s_PreviousVSyncCount;
	private static int s_PreviousTargetFrameRate;

	/// <summary>
	/// Stops waiting on the local display and runs to a timer instead. Safe to call when already holding.
	/// </summary>
	public static void Hold()
	{
		if (s_Held)
			return;

		s_Held = true;
		s_PreviousVSyncCount = QualitySettings.vSyncCount;
		s_PreviousTargetFrameRate = Application.targetFrameRate;

		//targetFrameRate is only honoured with vsync off, so both have to move together
		QualitySettings.vSyncCount = 0;
		Application.targetFrameRate = StreamingFrameRate;

		Debug.Log("RemotePlay: pacing frames by timer at " + StreamingFrameRate
				  + "fps while a viewer is watching, instead of by this machine's display");
	}

	/// <summary>
	/// Hands the display back its say over when a frame ends.
	/// </summary>
	public static void Release()
	{
		if (!s_Held)
			return;

		s_Held = false;

		QualitySettings.vSyncCount = s_PreviousVSyncCount;
		Application.targetFrameRate = s_PreviousTargetFrameRate;

		Debug.Log("RemotePlay: frame pacing handed back to the display"
				  + " (vSyncCount=" + s_PreviousVSyncCount
				  + " targetFrameRate=" + s_PreviousTargetFrameRate + ")");
	}
}
