using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "InputReader", menuName = "Game/Input Reader")]
public class InputReader : DescriptionBaseSO, GameInput.IGameplayActions, GameInput.IDialoguesActions, GameInput.IMenusActions, GameInput.ICheatsActions
{
	[Space]
	[SerializeField] private GameStateSO _gameStateManager;
	
	// Assign delegate{} to events to initialise them with an empty delegate
	// so we can skip the null check when we use them

	// Gameplay
	public event UnityAction JumpEvent = delegate { };
	public event UnityAction JumpCanceledEvent = delegate { };
	public event UnityAction AttackEvent = delegate { };
	public event UnityAction AttackCanceledEvent = delegate { };
	public event UnityAction InteractEvent = delegate { }; // Used to talk, pickup objects, interact with tools like the cooking cauldron
	public event UnityAction InventoryActionButtonEvent = delegate { };
	public event UnityAction SaveActionButtonEvent = delegate { };
	public event UnityAction ResetActionButtonEvent = delegate { };
	public event UnityAction<Vector2> MoveEvent = delegate { };
	public event UnityAction<Vector2, bool> CameraMoveEvent = delegate { };
	public event UnityAction<float> CameraZoomEvent = delegate { };
	public event UnityAction<Vector2> PointerClickEvent = delegate { };
	public event UnityAction EnableMouseControlCameraEvent = delegate { };
	public event UnityAction DisableMouseControlCameraEvent = delegate { };
	public event UnityAction StartedRunning = delegate { };
	public event UnityAction StoppedRunning = delegate { };

	// Shared between menus and dialogues
	public event UnityAction MoveSelectionEvent = delegate { };

	// Dialogues
	public event UnityAction AdvanceDialogueEvent = delegate { };

	// Menus
	public event UnityAction MenuMouseMoveEvent = delegate { };
	public event UnityAction MenuClickButtonEvent = delegate { };
	public event UnityAction MenuUnpauseEvent = delegate { };
	public event UnityAction MenuPauseEvent = delegate { };
	public event UnityAction MenuCloseEvent = delegate { };
	public event UnityAction OpenInventoryEvent = delegate { }; // Used to bring up the inventory
	public event UnityAction CloseInventoryEvent = delegate { }; // Used to bring up the inventory
	public event UnityAction<float> TabSwitched = delegate { };

	// Cheats (has effect only in the Editor)
	public event UnityAction CheatMenuEvent = delegate { };

	private GameInput _gameInput;

	private void OnEnable()
	{
		if (_gameInput == null)
		{
			_gameInput = new GameInput();

			_gameInput.Menus.SetCallbacks(this);
			_gameInput.Gameplay.SetCallbacks(this);
			_gameInput.Dialogues.SetCallbacks(this);
			_gameInput.Cheats.SetCallbacks(this);
		}

#if UNITY_EDITOR
	_gameInput.Cheats.Enable();
#endif
	}

	private void OnDisable()
	{
		DisableAllInput();
	}

	public void OnAttack(InputAction.CallbackContext context)
	{
		switch (context.phase)
		{
			case InputActionPhase.Performed:
				AttackEvent.Invoke();
				break;
			case InputActionPhase.Canceled:
				AttackCanceledEvent.Invoke();
				break;
		}
	}

	public void OnOpenInventory(InputAction.CallbackContext context)
	{
		if (context.phase == InputActionPhase.Performed)
			OpenInventoryEvent.Invoke();
	}

	// Lets the bag on the HUD ask for the inventory the same way the key does, so both go through the one
	// event and everything listening to it behaves identically
	public void RequestOpenInventory()
	{
		OpenInventoryEvent.Invoke();
	}
	public void OnCancel(InputAction.CallbackContext context)
	{
		if (context.phase == InputActionPhase.Performed)
			MenuCloseEvent.Invoke();
	}

	public void OnInventoryActionButton(InputAction.CallbackContext context)
	{
		if (context.phase == InputActionPhase.Performed)
			InventoryActionButtonEvent.Invoke();
	}

	public void OnSaveActionButton(InputAction.CallbackContext context)
	{
		if (context.phase == InputActionPhase.Performed)
			SaveActionButtonEvent.Invoke();
	}

	public void OnResetActionButton(InputAction.CallbackContext context)
	{
		if (context.phase == InputActionPhase.Performed)
			ResetActionButtonEvent.Invoke();
	}

	public void OnInteract(InputAction.CallbackContext context)
	{
		if ((context.phase == InputActionPhase.Performed)
			&& (_gameStateManager.CurrentGameState == GameState.Gameplay)) // Interaction is only possible when in gameplay GameState
			InteractEvent.Invoke();
	}

	// Lets the prompt on the HUD ask for the same thing the key asks for, gate and all, so a tap cannot
	// interact at a moment the key could not. What it turns into - talking, cooking, picking something up -
	// is settled by whatever is in reach, the same as with the key.
	public void RequestInteract()
	{
		if (_gameStateManager.CurrentGameState == GameState.Gameplay)
			InteractEvent.Invoke();
	}

	/// Swings without a key, for the button on the HUD. A viewer on a phone has no keyboard to reach the
	/// attack with now that the left button walks the character instead.
	///
	/// Combat counts as much as ordinary play here, and rather more: an enemy being awake is what puts the
	/// game in that state, and refusing to swing then is refusing at the one moment it is wanted. The key
	/// has always worked there, since the gameplay map stays enabled through combat.
	public void RequestAttack()
	{
		if (_gameStateManager.CurrentGameState == GameState.Gameplay
			|| _gameStateManager.CurrentGameState == GameState.Combat)
			AttackEvent.Invoke();
	}

	public void OnJump(InputAction.CallbackContext context)
	{
		if (context.phase == InputActionPhase.Performed)
			JumpEvent.Invoke();

		if (context.phase == InputActionPhase.Canceled)
			JumpCanceledEvent.Invoke();
	}

	public void OnMove(InputAction.CallbackContext context)
	{
		MoveEvent.Invoke(context.ReadValue<Vector2>());
	}

	public void OnRun(InputAction.CallbackContext context)
	{
		switch (context.phase)
		{
			case InputActionPhase.Performed:
				StartedRunning.Invoke();
				break;
			case InputActionPhase.Canceled:
				StoppedRunning.Invoke();
				break;
		}
	}

	public void OnPause(InputAction.CallbackContext context)
	{
		if (context.phase == InputActionPhase.Performed)
			MenuPauseEvent.Invoke();
	}

	public void OnRotateCamera(InputAction.CallbackContext context)
	{
		CameraMoveEvent.Invoke(context.ReadValue<Vector2>(), IsDeviceMouse(context));
	}

	public void OnMouseControlCamera(InputAction.CallbackContext context)
	{
		if (context.phase == InputActionPhase.Performed)
			EnableMouseControlCameraEvent.Invoke();

		if (context.phase == InputActionPhase.Canceled)
			DisableMouseControlCameraEvent.Invoke();
	}

	/// <summary>
	/// A click or tap somewhere in the world, carrying where on the screen it landed.
	/// </summary>
	/// <remarks>
	/// The position is read off the device that did the clicking rather than from Mouse.current, because
	/// there is more than one mouse while a remote viewer is connected and the one holding "current" is not
	/// always the one whose button went down.
	/// </remarks>
	public void OnPointerClick(InputAction.CallbackContext context)
	{
		if (context.phase != InputActionPhase.Performed)
			return;

		//Pointer rather than Mouse, so a touchscreen or a pen would be read the same way
		if (context.control.device is Pointer pointer)
			PointerClickEvent.Invoke(pointer.position.ReadValue());
	}

	public void OnZoomCamera(InputAction.CallbackContext context)
	{
		//The wheel reports how far it turned rather than where it rests, so a notch arrives as one value and
		//is gone by the next frame. Canceled carries the zero that follows and would only undo the notch.
		if (context.phase == InputActionPhase.Canceled)
			return;

		float scroll = context.ReadValue<float>();
		if (scroll != 0f)
			CameraZoomEvent.Invoke(scroll);
	}

	// Asked of the device's type rather than its name, so a second mouse counts too. The remote-play
	// router adds one of its own to carry the viewer's touches, and a name check missed it: the camera
	// then read those as stick movement and paced them by frame time instead of by how far they moved.
	private bool IsDeviceMouse(InputAction.CallbackContext context) => context.control.device is Mouse;

	public void OnMoveSelection(InputAction.CallbackContext context)
	{
		if (context.phase == InputActionPhase.Performed)
			MoveSelectionEvent.Invoke();
	}

	public void OnAdvanceDialogue(InputAction.CallbackContext context)
	{

		if (context.phase == InputActionPhase.Performed)
			AdvanceDialogueEvent.Invoke();
	}

	// Lets a click on the dialogue box move it on the same way the key does. No gate of its own: the box
	// is only on screen while somebody is talking, which is the same window the key has.
	public void RequestAdvanceDialogue()
	{
		AdvanceDialogueEvent.Invoke();
	}

	public void OnConfirm(InputAction.CallbackContext context)
	{
		if (context.phase == InputActionPhase.Performed)
			MenuClickButtonEvent.Invoke();
	}


	public void OnMouseMove(InputAction.CallbackContext context)
	{
		if (context.phase == InputActionPhase.Performed)
			MenuMouseMoveEvent.Invoke();
	}

	public void OnUnpause(InputAction.CallbackContext context)
	{
		if (context.phase == InputActionPhase.Performed)
			MenuUnpauseEvent.Invoke();
	}

	public void OnOpenCheatMenu(InputAction.CallbackContext context)
	{
		if (context.phase == InputActionPhase.Performed)
			CheatMenuEvent.Invoke();
	}

	public void EnableDialogueInput()
	{
		_gameInput.Menus.Enable();
		_gameInput.Gameplay.Disable();
		_gameInput.Dialogues.Enable();
	}

	public void EnableGameplayInput()
	{
		_gameInput.Menus.Disable();
		_gameInput.Dialogues.Disable();
		_gameInput.Gameplay.Enable();
	}

	public void EnableMenuInput()
	{
		_gameInput.Dialogues.Disable();
		_gameInput.Gameplay.Disable();

		_gameInput.Menus.Enable();
	}

	public void DisableAllInput()
	{
		_gameInput.Gameplay.Disable();
		_gameInput.Menus.Disable();
		_gameInput.Dialogues.Disable();
	}

	public void OnChangeTab(InputAction.CallbackContext context)
	{
		if (context.phase == InputActionPhase.Performed)
			TabSwitched.Invoke(context.ReadValue<float>());
	}

	public bool LeftMouseDown() => Mouse.current.leftButton.isPressed;

	public void OnClick(InputAction.CallbackContext context)
	{

	}

	public void OnSubmit(InputAction.CallbackContext context)
	{

	}

	public void OnPoint(InputAction.CallbackContext context)
	{

	}
	
	public void OnRightClick(InputAction.CallbackContext context)
	{

	}

	public void OnNavigate(InputAction.CallbackContext context)
	{

	}

	public void OnCloseInventory(InputAction.CallbackContext context)
	{
		CloseInventoryEvent.Invoke();
	}
}
