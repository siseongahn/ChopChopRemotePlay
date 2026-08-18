using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// Feeds what the viewer does on their phone into the game as ordinary input.
///
/// The events are pushed onto a keyboard and a mouse of our own, added to the input system alongside
/// the real ones, so every existing binding keeps working untouched: &lt;Keyboard&gt;/w still drives Move,
/// &lt;Mouse&gt;/delta still turns the camera, and the action asset, InputReader and the UI module need no
/// changes.
///
/// RemotePlay calls in from its own thread and the input system is main-thread only, so events are
/// queued there and drained here.
public class RemotePlayInputRouter : MonoBehaviour
{
	public enum Phase { Down, Move, Up, Cancel }

	private enum Kind { Key, Touch, Wheel }

	/// A touch carries no button of its own, so what it means depends on what the game is showing: over a
	/// menu it works as a pointer, and in play it either turns the camera or lands a hit.
	private enum TouchRole { Camera, Pointer }

	//Gates the camera drag, per MouseControlCamera's binding
	private const MouseButton CameraDragButton = MouseButton.Right;
	//Attack in play, and the UI's click everywhere else
	private const MouseButton ClickButton = MouseButton.Left;

	//How far a finger has to travel, in stream units, before it counts as a drag rather than a tap
	private const float DragThreshold = 20f;

	//Unique to the Gameplay map, so its presence tells us the game is being played rather than navigated
	private const string GameplayOnlyAction = "MouseControlCamera";

	//What windows counts as one notch of the wheel, which is the scale the input system expects too
	private const float WheelNotch = 120f;


	private static readonly ConcurrentQueue<Event> s_Queue = new ConcurrentQueue<Event>();
	private static RemotePlayInputRouter s_Instance;

	private Keyboard _keyboard;
	private Mouse _mouse;

	private readonly HashSet<Key> _heldKeys = new HashSet<Key>();
	private readonly List<InputAction> _enabledActions = new List<InputAction>();

	private TouchRole _touchRole;
	private bool _touching;
	private bool _dragging;
	private Vector2 _touchStart;
	private Vector2 _lastTouch;

	private bool _mouseButtonHeld;
	private MouseButton _heldMouseButton;

	//What the last touch was mapped under, so only changes get reported rather than every touch
	private string _lastSituation;
	private bool _lastScreenUsable = true;

	/// Brings the router up without a scene object to hang it on: it only exists to relay input, and the
	/// Initialization scene that would otherwise host it is unloaded once the game boots.
	public static void Spawn()
	{
		if (s_Instance != null)
			return;

		var go = new GameObject("RemotePlayInputRouter");
		DontDestroyOnLoad(go);
		s_Instance = go.AddComponent<RemotePlayInputRouter>();
	}

	public static void EnqueueKey(int virtualKey, Phase phase)
	{
		s_Queue.Enqueue(new Event { kind = Kind.Key, virtualKey = virtualKey, phase = phase });
	}

	public static void EnqueueTouch(Vector2 streamPoint, Phase phase)
	{
		s_Queue.Enqueue(new Event { kind = Kind.Touch, point = streamPoint, phase = phase });
	}

	public static void EnqueueWheel(Vector2 streamPoint, Phase phase)
	{
		s_Queue.Enqueue(new Event { kind = Kind.Wheel, point = streamPoint, phase = phase });
	}

	private void Awake()
	{
		//Named so they are easy to tell apart from the machine's own devices while debugging
		_keyboard = InputSystem.AddDevice<Keyboard>("RemotePlayKeyboard");
		_mouse = InputSystem.AddDevice<Mouse>("RemotePlayMouse");
	}

	private void OnDestroy()
	{
		if (_keyboard != null)
			InputSystem.RemoveDevice(_keyboard);

		if (_mouse != null)
			InputSystem.RemoveDevice(_mouse);
	}

	private void Update()
	{
		bool keysChanged = false;

		//Windows locking, or the game being switched away from, can leave the window with no size worth
		//mapping against. A touch measured off that lands somewhere meaningless, so the pointer is let go
		//of instead and the gesture dropped. Keys do not depend on the screen and carry on.
		bool screenUsable = Screen.width > 0 && Screen.height > 0;

		//Said out loud, because a dropped touch is otherwise indistinguishable from one that never came
		if (screenUsable != _lastScreenUsable)
		{
			_lastScreenUsable = screenUsable;

			if (screenUsable)
				Debug.Log("RemotePlay: screen usable again at " + Screen.width + "x" + Screen.height
						  + ", touches are being mapped");
			else
				Debug.LogWarning("RemotePlay: screen is " + Screen.width + "x" + Screen.height
								 + ", so touches are being dropped rather than mapped somewhere wrong");
		}

		while (s_Queue.TryDequeue(out Event e))
		{
			switch (e.kind)
			{
				case Kind.Key:
					keysChanged |= ApplyKey(e);
					break;

				case Kind.Touch:
					if (screenUsable)
						ApplyTouch(e);
					else
						CancelTouch();
					break;

				case Kind.Wheel:
					if (screenUsable)
						ApplyWheel(e);
					break;
			}
		}

		//One state event for the whole frame: a keyboard state carries every key at once, so sending it
		//per key press would only overwrite itself
		if (keysChanged)
			SendKeyboardState();
	}

	private bool ApplyKey(Event e)
	{
		if (!TryMapKey(e.virtualKey, out Key key))
		{
			Debug.Log("RemotePlay: no mapping for virtual key 0x" + e.virtualKey.ToString("X2"));
			return false;
		}

		//Only a press holds a key; everything else lets go, so a cancelled key cannot stay stuck down. A
		//held key repeats Down and an Up can arrive for a key we never saw, so the set decides whether
		//anything actually changed.
		return e.phase == Phase.Down ? _heldKeys.Add(key) : _heldKeys.Remove(key);
	}

	private void SendKeyboardState()
	{
		var state = new KeyboardState();
		foreach (Key key in _heldKeys)
			state.Set(key, true);

		InputSystem.QueueStateEvent(_keyboard, state);
	}

	private void ApplyTouch(Event e)
	{
		if (e.phase == Phase.Down)
		{
			//Settled when the finger goes down and held for the rest of the gesture, so a menu opening
			//midway cannot change what it means
			_touchRole = IsGameplayLive() ? TouchRole.Camera : TouchRole.Pointer;
			_touching = true;
			_dragging = false;
			_touchStart = e.point;
			_lastTouch = e.point;

			ReportMapping(e.point);

			//A menu press has to land the moment the finger does, so the UI sees the button go down at that
			//spot. In play we hold off: only travel means the camera, and a finger that never travels is a
			//tap, which we cannot know about until it lifts.
			if (_touchRole == TouchRole.Pointer)
				QueueMouse(e.point, Vector2.zero, ClickButton, true);

			return;
		}

		//Movement outside a Down..Up pair has nothing to be relative to
		if (!_touching)
			return;

		float scale = StreamToScreen();
		Vector2 delta = new Vector2((e.point.x - _lastTouch.x) * scale,
									-(e.point.y - _lastTouch.y) * scale);
		_lastTouch = e.point;

		if (e.phase == Phase.Move)
		{
			if (_touchRole == TouchRole.Pointer)
			{
				//The UI wants where the finger is, not how fast it got there
				QueueMouse(e.point, Vector2.zero, ClickButton, true);
				return;
			}

			//Far enough along to be a drag. Measured from where the finger landed, while the delta is
			//measured from the previous point, so opening the gate does not jerk the camera.
			if (!_dragging && (e.point - _touchStart).sqrMagnitude >= DragThreshold * DragThreshold)
				_dragging = true;

			if (_dragging)
				QueueMouse(e.point, delta, CameraDragButton, true);

			return;
		}

		//Phase.Up, or Phase.Cancel where the pointer left the view
		_touching = false;

		if (_touchRole == TouchRole.Pointer)
		{
			QueueMouse(e.point, Vector2.zero, ClickButton, false);
			return;
		}

		if (_dragging)
		{
			QueueMouse(e.point, Vector2.zero, CameraDragButton, false);
			return;
		}

		//Nothing was pressed and nothing travelled, and a finger that left the view was never a tap
		if (e.phase == Phase.Cancel)
			return;

		//It never travelled, so it was a tap: in play that is a swing rather than a camera drag. Press and
		//release land in the same batch, which is all a button action needs to report a press.
		QueueMouse(e.point, Vector2.zero, ClickButton, true);
		QueueMouse(e.point, Vector2.zero, ClickButton, false);
	}

	/// A wheel event carries where the pointer sat and which way it turned; one event is one notch.
	private void ApplyWheel(Event e)
	{
		//Only up and down mean anything here - a wheel never reports movement
		float notch = e.phase == Phase.Down ? -WheelNotch : e.phase == Phase.Up ? WheelNotch : 0f;
		if (notch == 0f)
			return;

		var state = new MouseState
		{
			position = ToScreenPosition(e.point),
			scroll = new Vector2(0f, notch)
		};

		//A state event carries every button at once, so a wheel turned partway through a drag has to
		//repeat the held button or it would read as the finger coming up. The drag's own baseline is left
		//alone, so the next move still measures from where the finger actually was.
		if (_mouseButtonHeld)
			state = state.WithButton(_heldMouseButton, true);

		InputSystem.QueueStateEvent(_mouse, state);
	}

	/// Windows locking, alt-tabbing and the like swallow the release that would otherwise arrive, which
	/// would leave a key held or the camera gate open for good. Everything is let go of on the way out.
	private void OnApplicationFocus(bool hasFocus)
	{
		if (hasFocus)
			return;

		if (_heldKeys.Count > 0)
		{
			_heldKeys.Clear();
			SendKeyboardState();
		}

		CancelTouch();
	}

	/// Drops the gesture and lets go of whatever it was holding, without reading it as a tap.
	private void CancelTouch()
	{
		_touching = false;
		_dragging = false;

		//Focus can come and go around the device being added and taken away again
		if (!_mouseButtonHeld || _mouse == null || !_mouse.added)
			return;

		//Left where it is rather than sent to the origin, so letting go cannot click something else
		var state = new MouseState { position = _mouse.position.ReadValue() };
		InputSystem.QueueStateEvent(_mouse, state.WithButton(_heldMouseButton, false));

		_mouseButtonHeld = false;
	}

	private void QueueMouse(Vector2 streamPoint, Vector2 delta, MouseButton button, bool pressed)
	{
		_mouseButtonHeld = pressed;
		_heldMouseButton = button;

		var state = new MouseState
		{
			position = ToScreenPosition(streamPoint),
			delta = delta
		};

		InputSystem.QueueStateEvent(_mouse, state.WithButton(button, pressed));
	}

	/// Says where a touch was aimed and where the pointer the UI reads actually is.
	///
	/// Two things can put those apart. The screen can stop matching what the stream is still sending, in
	/// which case the reported point maps to the wrong place. Or the UI can be reading a different mouse
	/// altogether: it folds every mouse into one pointer by default, so the machine's own mouse moving
	/// decides where our button press is judged to have happened.
	private void ReportMapping(Vector2 streamPoint)
	{
		int mice = 0;
		Mouse other = null;

		foreach (InputDevice device in InputSystem.devices)
		{
			if (!(device is Mouse mouse))
				continue;

			mice++;

			if (mouse != _mouse)
				other = mouse;
		}

		//Only the things that decide whether a touch lands where it was aimed. The point itself is left
		//out: it changes with every touch, and reporting on that put the log past the point of being
		//readable and hid the moment something actually went wrong.
		string situation = "screen " + Screen.width + "x" + Screen.height
						   + " | focused " + Application.isFocused
						   + " | mice " + mice
						   + " | current is " + (Mouse.current == _mouse ? "ours" : "the machine's");

		if (situation == _lastSituation)
			return;

		_lastSituation = situation;

		Debug.Log("RemotePlay: touch " + streamPoint.ToString("F1")
				  + " aimed at " + ToScreenPosition(streamPoint).ToString("F1")
				  + " | " + situation
				  + " | ours reads " + _mouse.position.ReadValue().ToString("F1")
				  + " | machine's reads " + (other != null ? other.position.ReadValue().ToString("F1") : "none"));
	}

	/// Stream pixels to game pixels. Only the ratio between the two matters, so this holds whatever the
	/// window is resized to, where a measured stream size would not.
	private static float StreamToScreen()
	{
		float scale = HiveRemotePlayEvents.StreamScale;
		return scale > 0f ? 1f / scale : 1f;
	}

	//Stream coordinates run down the screen, the input system's run up it
	private static Vector2 ToScreenPosition(Vector2 streamPoint)
	{
		float scale = StreamToScreen();
		return new Vector2(streamPoint.x * scale, Screen.height - streamPoint.y * scale);
	}

	/// True while the game is being played rather than sitting in a menu or a dialogue. Read off the
	/// input system's enabled actions so the router needs no reference to the InputReader asset.
	private bool IsGameplayLive()
	{
		_enabledActions.Clear();
		InputSystem.ListEnabledActions(_enabledActions);

		for (int i = 0; i < _enabledActions.Count; i++)
		{
			if (_enabledActions[i].name == GameplayOnlyAction)
				return true;
		}

		return false;
	}

	/// Windows virtual-key codes to the input system's keys. Covers what the game binds plus the keys
	/// around them; anything else is logged rather than guessed at.
	private static bool TryMapKey(int vk, out Key key)
	{
		//0x41..0x5A are A..Z, and Key.A..Key.Z run in the same order
		if (vk >= 0x41 && vk <= 0x5A)
		{
			key = Key.A + (vk - 0x41);
			return true;
		}

		switch (vk)
		{
			case 0x08: key = Key.Backspace; return true;
			case 0x09: key = Key.Tab; return true;
			case 0x0D: key = Key.Enter; return true;
			//The generic modifiers have no side of their own; the game binds "shift", which either satisfies
			case 0x10: case 0xA0: key = Key.LeftShift; return true;
			case 0xA1: key = Key.RightShift; return true;
			case 0x11: case 0xA2: key = Key.LeftCtrl; return true;
			case 0xA3: key = Key.RightCtrl; return true;
			case 0x12: case 0xA4: key = Key.LeftAlt; return true;
			case 0xA5: key = Key.RightAlt; return true;
			case 0x1B: key = Key.Escape; return true;
			case 0x20: key = Key.Space; return true;
			case 0x25: key = Key.LeftArrow; return true;
			case 0x26: key = Key.UpArrow; return true;
			case 0x27: key = Key.RightArrow; return true;
			case 0x28: key = Key.DownArrow; return true;
			//Digits are not contiguous in the Key enum: Digit1..Digit9 come before Digit0
			case 0x30: key = Key.Digit0; return true;
			case 0x31: key = Key.Digit1; return true;
			case 0x32: key = Key.Digit2; return true;
			case 0x33: key = Key.Digit3; return true;
			case 0x34: key = Key.Digit4; return true;
			case 0x35: key = Key.Digit5; return true;
			case 0x36: key = Key.Digit6; return true;
			case 0x37: key = Key.Digit7; return true;
			case 0x38: key = Key.Digit8; return true;
			case 0x39: key = Key.Digit9; return true;
			//The windows keys and the menu key beside them. Pressing these on the virtual keyboard cannot
			//reach the desktop, so they land in the game like any other key rather than locking anything.
			case 0x5B: key = Key.LeftMeta; return true;
			case 0x5C: key = Key.RightMeta; return true;
			case 0x5D: key = Key.ContextMenu; return true;
			//The OEM keys a phone keyboard can reach
			case 0xC0: key = Key.Backquote; return true;
			case 0xBA: key = Key.Semicolon; return true;
			case 0xBB: key = Key.Equals; return true;
			case 0xBC: key = Key.Comma; return true;
			case 0xBD: key = Key.Minus; return true;
			case 0xBE: key = Key.Period; return true;
			case 0xBF: key = Key.Slash; return true;
			case 0xDB: key = Key.LeftBracket; return true;
			case 0xDC: key = Key.Backslash; return true;
			case 0xDD: key = Key.RightBracket; return true;
			case 0xDE: key = Key.Quote; return true;
			default: key = Key.None; return false;
		}
	}

	private struct Event
	{
		public Kind kind;
		public int virtualKey;
		public Vector2 point;
		public Phase phase;
	}
}
