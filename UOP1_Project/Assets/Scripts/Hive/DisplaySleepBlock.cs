using UnityEngine;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Keeps the screen on for as long as somebody is watching it from somewhere else.
/// </summary>
/// <remarks>
/// Nobody touches the keyboard of a machine being played remotely, so Windows decides it is idle and turns
/// the display off. What follows is measurable: frame delivery went from 16.67ms to 78.5ms on this machine
/// once the screen went dark, and the game carried on reporting 16.65ms per frame regardless, so the
/// simulation ran at about a fifth of real time - smoothly, since every frame was handed the same step.
/// The picture crawled while the audio kept perfect time.
///
/// Asking Windows to keep the display on is what stops that, and it is the streamer's business to ask: the
/// requirement comes from there being a viewer, which nothing else in the process knows about.
///
/// Two things worth knowing about the API. The request belongs to the thread that makes it, so this has to
/// be called from the main thread and not from the plugin's callback thread, which comes and goes. And
/// ES_CONTINUOUS makes it stick until it is taken back rather than needing a heartbeat.
///
/// ES_DISPLAY_REQUIRED resets the display idle timer, which on a machine that sleeps the ordinary way also
/// brings a screen that has already gone dark back on. It is ignored on Modern Standby systems; this one
/// reports S3 rather than S0 low power idle, so it holds here. Somewhere it does not, the display may sleep
/// anyway and the slow motion would come back with it.
/// </remarks>
public static class DisplaySleepBlock
{
	private static bool s_Held;

	/// <summary>
	/// Turns the display back on if it has gone dark, and keeps it on. Safe to call when already holding.
	/// </summary>
	/// <remarks>
	/// Main thread only - the request is tied to the calling thread.
	///
	/// Two calls rather than one, because they are asking different things. Without ES_CONTINUOUS the flags
	/// mean "right now", which is what resets the display idle timer and brings a dark screen back; with it
	/// they mean "until I say otherwise", which is what stops it going dark again. A viewer connecting to a
	/// machine that has already turned its screen off needs both.
	///
	/// Neither can help if the machine itself has suspended - nothing in this process is running to ask. On
	/// this machine that does not arise on mains power, where system sleep is set to never and only the
	/// display turns off, but on battery it sleeps after ten minutes and only the network card could wake it.
	/// </remarks>
	public static void Hold()
	{
		if (s_Held)
			return;

		s_Held = true;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
		//"Wake up now"
		Ask(ExecutionState.DisplayRequired | ExecutionState.SystemRequired);

		//"And stay awake"
		if (Ask(ExecutionState.Continuous | ExecutionState.DisplayRequired | ExecutionState.SystemRequired))
			Debug.Log("RemotePlay: woke the display and asked windows to keep it awake for the viewer");
#else
		Debug.Log("RemotePlay: keeping the display awake is windows-player only; nothing to do here");
#endif
	}

	/// <summary>
	/// Gives the request back, letting the machine idle as it normally would.
	/// </summary>
	public static void Release()
	{
		if (!s_Held)
			return;

		s_Held = false;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
		if (Ask(ExecutionState.Continuous))
			Debug.Log("RemotePlay: let go of the display, the machine can idle again");
#endif
	}

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
	private static bool Ask(ExecutionState state)
	{
		try
		{
			//Zero back means it refused. The return is otherwise the state we replaced, which we have no use
			//for: this is the only thing in the process asking.
			if (SetThreadExecutionState(state) != 0)
				return true;

			Debug.LogWarning("RemotePlay: windows would not take the power request (state " + state + ")");
			return false;
		}
		catch (Exception e)
		{
			Debug.LogWarning("RemotePlay: asking about the display failed: " + e.Message);
			return false;
		}
	}

	[Flags]
	private enum ExecutionState : uint
	{
		SystemRequired = 0x00000001,
		DisplayRequired = 0x00000002,
		Continuous = 0x80000000,
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);
#endif
}
