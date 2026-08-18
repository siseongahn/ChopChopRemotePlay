using System;
using System.Globalization;
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
///   Control - the viewer's key presses and touches, which is what actually drives the game
///
/// Only the marshalling is windows-player-only: RemotePlayDll is laid down next to the built
/// executable and resolved against the working directory, so the game has to be started from the
/// build folder. Parsing stays platform-agnostic so it compiles and reads the same everywhere.
public static class HiveRemotePlayEvents
{
	//The plugin reports touches in a fixed stream space rather than in the game's own pixels. Measured
	//by tapping the four corners of the stream: the extremes came back just inside 1680x1050, which is
	//exactly 1.6 like the client area, so touches map onto the client with no letterboxing.
	public const float StreamWidth = 1680f;
	public const float StreamHeight = 1050f;

	[RuntimeInitializeOnLoadMethod]
	private static void Register()
	{
		RemotePlayInputRouter.Spawn();

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

	/// Reads one payload and hands anything actionable to the router.
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
			case "Control":
				HandleControl(json);
				break;

			case "Message":
				SimpleValue chat = JsonUtility.FromJson<SimplePayload>(json)?.eventValue;
				Debug.Log("RemotePlay: chat (" + chat?.action + "): " + DecodeMessage(chat?.value));
				break;

			case "Event":
				Debug.Log("RemotePlay: status " + JsonUtility.FromJson<SimplePayload>(json)?.eventValue?.value);
				break;

			default:
				Debug.Log("RemotePlay: unhandled event type " + type + ": " + json);
				break;
		}
	}

	//eventValue.value is an object here rather than the string the other two events carry, which is why
	//control needs a shape of its own to deserialize into.
	private static void HandleControl(string json)
	{
		ControlInner control = JsonUtility.FromJson<ControlPayload>(json)?.eventValue?.value;
		if (control?.controlValue == null)
			return;

		string value = control.controlValue.value;
		RemotePlayInputRouter.Phase phase;

		switch (control.controlValue.action)
		{
			case "Down": phase = RemotePlayInputRouter.Phase.Down; break;
			case "Move": phase = RemotePlayInputRouter.Phase.Move; break;
			case "Up": phase = RemotePlayInputRouter.Phase.Up; break;
			//The pointer left the streamed view. Whatever it was holding has to be let go, or the button
			//stays down for good and the camera gate never closes.
			case "Out": phase = RemotePlayInputRouter.Phase.Cancel; break;
			default:
				Debug.Log("RemotePlay: unhandled control action " + control.controlValue.action + ": " + json);
				return;
		}

		switch (control.controlType)
		{
			//A windows virtual-key code in hex. Presses overlap, so the router tracks a set of held keys
			//rather than a single one.
			case "Key":
				if (TryParseHex(value, out int keyCode))
					RemotePlayInputRouter.EnqueueKey(keyCode, phase);
				else
					Debug.LogWarning("RemotePlay: could not read key code " + value);
				break;

			//"X#Y" in stream space. This is a touch, not a mouse: no button to speak of, and no movement
			//outside a Down..Up pair.
			case "Click":
				if (TryParsePoint(value, out Vector2 point))
					RemotePlayInputRouter.EnqueueTouch(point, phase);
				else
					Debug.LogWarning("RemotePlay: could not read touch point " + value);
				break;

			//Same "X#Y" as a touch, but the point is only where the pointer sat and the action is the
			//direction scrolled. One event is one notch, and holding a scroll repeats it.
			case "Wheel":
				if (TryParsePoint(value, out Vector2 wheelPoint))
					RemotePlayInputRouter.EnqueueWheel(wheelPoint, phase);
				else
					Debug.LogWarning("RemotePlay: could not read wheel point " + value);
				break;

			default:
				Debug.Log("RemotePlay: unhandled control type " + control.controlType + ": " + json);
				break;
		}
	}

	private static bool TryParseHex(string value, out int result)
	{
		result = 0;
		if (string.IsNullOrEmpty(value))
			return false;

		string digits = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.Substring(2) : value;
		return int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
	}

	private static bool TryParsePoint(string value, out Vector2 result)
	{
		result = Vector2.zero;
		if (string.IsNullOrEmpty(value))
			return false;

		string[] parts = value.Split('#');
		if (parts.Length != 2)
			return false;

		//Invariant culture because the payload always uses a dot, whatever the machine is set to
		if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
			|| !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
			return false;

		result = new Vector2(x, y);
		return true;
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

	[Serializable]
	private class ControlPayload
	{
		public ControlOuter eventValue;
	}

	[Serializable]
	private class ControlOuter
	{
		public ControlInner value;
	}

	[Serializable]
	private class ControlInner
	{
		public string controlType;
		public ControlDetail controlValue;
	}

	[Serializable]
	private class ControlDetail
	{
		public string value;
		public string action;
	}
}
