using System;
using UnityEngine;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System.Runtime.InteropServices;
using AOT;
#endif

/// Takes what Hive's RemotePlay plugin reports to the host game and turns it into something the game
/// can act on.
///
/// Three kinds of event come over the one callback:
///   Event   - the stream connecting and disconnecting
///   Message - chat the viewer typed, base64'd so it survives as utf-8
///   Control - the viewer's key presses and touches
///
/// Control is ignored. The same input is already arriving as ordinary windows input: RemotePlay's
/// HiveVirtualInput helper injects it with SendInput, onto the machine's own mouse and keyboard, so
/// acting on the callback as well would deliver everything twice. See RemotePlaySession.
///
/// Only the marshalling is windows-player-only: RemotePlayDll is laid down next to the built
/// executable and resolved against the working directory, so the game has to be started from the
/// build folder. Parsing stays platform-agnostic so it compiles and reads the same everywhere.
public static class HiveRemotePlayEvents
{
	//Control events arrive continuously while somebody is playing, so their being ignored is said once
	//rather than per event
	private static bool s_SaidControlIsIgnored;

	[RuntimeInitializeOnLoadMethod]
	private static void Register()
	{
		RemotePlaySession.Spawn();

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
		//RemotePlay keeps the pointer, and nothing else holds the delegate once we return, so it has
		//to live in a static or it gets collected and the first event takes the process down
		s_Callback = OnRemotePlayEvent;

		try
		{
			RegisterCallback(s_Callback);
			Debug.Log("RemotePlay: callback registered");
		}
		catch (DllNotFoundException e)
		{
			Debug.LogWarning("RemotePlay: " + NativeLibrary + " did not load, no events will arrive: " + e.Message);
		}
#endif
	}

	/// Reads one payload and hands anything actionable to the session.
	///
	/// The 'type' argument does not tell the three kinds apart on its own - chat arrives as 0, but both
	/// status and control arrive as 1 - so the eventType field is what we branch on.
	public static void HandlePayload(int type, string json)
	{
		if (string.IsNullOrEmpty(json))
			return;

		string eventType = JsonUtility.FromJson<Envelope>(json)?.eventType;

		switch (eventType)
		{
			//Input comes in through windows, not through here. The event is still worth one thing: it means
			//somebody is at the other end, which is how a game started into a live session finds out.
			case "Control":
				if (!s_SaidControlIsIgnored)
				{
					s_SaidControlIsIgnored = true;
					Debug.Log("RemotePlay: control events are arriving and are being ignored on purpose;"
							  + " input comes in through windows from HiveVirtualInput");
				}

				RemotePlaySession.NoteActivity();
				break;

			case "Message":
				SimpleValue chat = JsonUtility.FromJson<SimplePayload>(json)?.eventValue;
				Debug.Log("RemotePlay: chat (" + chat?.action + "): " + DecodeMessage(chat?.value));
				break;

			case "Event":
				string status = JsonUtility.FromJson<SimplePayload>(json)?.eventValue?.value;
				Debug.Log("RemotePlay: status " + status);

				if (status == "REMOTE_PLAY_CONNECTED")
					RemotePlaySession.SetConnected(true);
				else if (status == "REMOTE_PLAY_DISCONNECTED")
					RemotePlaySession.SetConnected(false);

				break;

			default:
				Debug.Log("RemotePlay: unhandled event type " + type + ": " + json);
				break;
		}
	}

	private static string DecodeMessage(string value)
	{
		if (string.IsNullOrEmpty(value))
			return string.Empty;

		try
		{
			return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value));
		}
		catch (FormatException)
		{
			return value + " (not base64)";
		}
	}

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
	private const string NativeLibrary = "plugins/RemotePlayDll";

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void RemotePlayCallbackType(int type, IntPtr data);

	[DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
	private static extern void RegisterCallback(RemotePlayCallbackType callback);

	static RemotePlayCallbackType s_Callback;

	/// RemotePlay calls this from its own thread, and an exception thrown back into native code takes
	/// the process with it, so the whole body stays inside the try.
	[MonoPInvokeCallback(typeof(RemotePlayCallbackType))]
	static void OnRemotePlayEvent(int type, IntPtr data)
	{
		try
		{
			//Every bit of text in the payload is base64'd, so the JSON itself is plain ascii
			HandlePayload(type, data == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(data));
		}
		catch (Exception e)
		{
			Debug.LogError("RemotePlay: handling the event failed: " + e);
		}
	}
#endif

	[Serializable]
	private class Envelope
	{
		public string version;
		public string eventType;
	}

	[Serializable]
	private class SimplePayload
	{
		public SimpleValue eventValue;
	}

	[Serializable]
	private class SimpleValue
	{
		public string value;
		public string action;
	}
}
