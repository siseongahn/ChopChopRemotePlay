using UnityEngine;

/// Holds the machine awake and the clock steady for as long as a viewer is watching.
///
/// Input is deliberately not this class's business. RemotePlay ships HiveVirtualInput, which injects
/// the viewer's touches and keys with SendInput, so they arrive as ordinary windows input on the
/// machine's own mouse and keyboard. Every binding, the UI module and the event system see a real
/// device and need nothing from us - which is the point of the SendInput route, and why the control
/// events that carry the same input over the plugin's callback are ignored rather than acted on.
///
/// Note that this leaves the game's own mouse and keyboard alone. An earlier route added devices of
/// its own and switched the machine's mouse off for the length of a session, to stop the desk mouse
/// and the viewer fighting over one pointer. Doing that here would switch off the very device the
/// injected input arrives on.
public class RemotePlaySession : MonoBehaviour
{
	private static RemotePlaySession s_Instance;

	//Written from RemotePlay's thread, read on ours
	private static volatile bool s_Connected;
	private static volatile bool s_ConnectionKnown;

	//What we have already acted on, so a session coming or going is handled once
	private bool _handled;

	/// Brings the watcher up without a scene object to hang it on: the Initialization scene that would
	/// otherwise host it is unloaded once the game boots.
	public static void Spawn()
	{
		if (s_Instance != null)
			return;

		var go = new GameObject("RemotePlaySession");
		DontDestroyOnLoad(go);
		s_Instance = go.AddComponent<RemotePlaySession>();
	}

	/// Told when a viewer joins or leaves. Called from RemotePlay's thread, so it only leaves a note for
	/// Update to act on rather than touching anything here.
	public static void SetConnected(bool connected)
	{
		s_Connected = connected;
		s_ConnectionKnown = true;
	}

	/// Takes an arriving control event as proof that somebody is at the other end, but only while nothing
	/// has said otherwise.
	///
	/// The event itself is not acted on - the input it describes is already on its way in through windows.
	/// It is only evidence that a viewer is there, and that is worth having: a game started into a session
	/// that was already up never hears it begin, because RemotePlay only reports the change, so waiting for
	/// that report would leave the display and the clock unattended for the whole session. Once a join or a
	/// leave has actually been reported, that is what counts - events keep arriving after a viewer has gone.
	public static void NoteActivity()
	{
		if (s_ConnectionKnown)
			return;

		s_Connected = true;
		s_ConnectionKnown = true;
	}

	private void Update()
	{
		if (!s_ConnectionKnown || s_Connected == _handled)
			return;

		_handled = s_Connected;

		//On this thread rather than the one the status arrived on: the power request belongs to whichever
		//thread asks, and the plugin's callback thread is not ours to depend on. Without this the screen
		//goes dark on an untouched machine and the game drops to a fifth of real speed, smoothly, while
		//the audio keeps time.
		if (s_Connected)
		{
			DisplaySleepBlock.Hold();

			//And stop taking the local display's word for when a frame is done. A power request can keep
			//Windows from idling the screen out, but not a hand on the monitor's power button, and the clock
			//the game moves by should not depend on either.
			RemotePlayFramePacing.Hold();
		}
		else
		{
			DisplaySleepBlock.Release();
			RemotePlayFramePacing.Release();
		}
	}

	private void OnDestroy()
	{
		//Quitting mid-session would otherwise leave the machine unable to idle, since the request outlives
		//nothing on its own
		DisplaySleepBlock.Release();
		RemotePlayFramePacing.Release();
	}
}
