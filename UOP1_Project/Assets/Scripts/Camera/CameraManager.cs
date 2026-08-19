using System;
using UnityEngine;
using Cinemachine;
using System.Collections;

public class CameraManager : MonoBehaviour
{
	public InputReader inputReader;
	public Camera mainCamera;
	public CinemachineFreeLook freeLookVCam;
	public CinemachineImpulseSource impulseSource;
	private bool _isRMBPressed;

	[SerializeField][Range(.5f, 3f)] private float _speedMultiplier = 1f; //TODO: make this modifiable in the game settings

	[Tooltip("Damps down how far a mouse drag turns the camera. Only the mouse: a stick and the keys are " +
			 "already paced by how long they are held, where a drag is not.")]
	[SerializeField][Range(.1f, 1f)] private float _mouseSensitivity = .3f;

	[Header("Zoom")]
	[Tooltip("How near and how far the wheel may pull the camera, as a share of the distance the rig was " +
			 "authored at. 1 is where the rig sits with no zoom applied.")]
	[SerializeField][Range(.2f, 1f)] private float _minZoom = .55f;
	[SerializeField][Range(1f, 3f)] private float _maxZoom = 1.7f;

	[Tooltip("How much of a wheel turn becomes zoom. A notch reports 120 on windows, and the viewer's wheel " +
			 "comes through as the same, so one notch moves the camera by 120 times this.")]
	[SerializeField][Range(.0002f, .01f)] private float _zoomSpeed = .0015f;

	[Tooltip("How quickly the camera settles on the distance the wheel asked for. Higher arrives sooner.")]
	[SerializeField][Range(1f, 30f)] private float _zoomSmoothing = 12f;

	[SerializeField] private TransformAnchor _cameraTransformAnchor = default;
	[SerializeField] private TransformAnchor _protagonistTransformAnchor = default;

	[Header("Listening on channels")]
	[Tooltip("The CameraManager listens to this event, fired by protagonist GettingHit state, to shake camera")]
	[SerializeField] private VoidEventChannelSO _camShakeEvent = default;

	[Tooltip("Raised with true while an enemy is awake and after it, so the camera can stand back for a fight")]
	[SerializeField] private BoolEventChannelSO _combatStateEvent = default;

	[Tooltip("How far back a fight pulls the camera, as a share of the distance the rig was authored at. " +
			 "Read as a floor rather than a setting: a player who has already zoomed further out is left alone.")]
	[SerializeField][Range(1f, 3f)] private float _combatZoom = 1.6f;

	private bool _cameraMovementLock = false;

	//The rig as it was authored. Zoom is a share of this rather than a nudge to whatever the orbits currently
	//hold, so repeated notches cannot drift and 1 always means exactly where the rig started.
	private CinemachineFreeLook.Orbit[] _authoredOrbits;
	private float _zoom = 1f;

	//What the wheel asked for, kept apart from where the camera actually ends up: a fight can stand the
	//camera further back without overwriting what the player chose, and they get it back when the fight ends.
	private float _zoomWanted = 1f;
	private bool _inCombat;

	/// Where the camera should be sitting, once a fight has had its say.
	///
	/// The larger of the two rather than the combat distance outright, so a player who likes the camera well
	/// back is not pulled in by a fight starting.
	private float ZoomTarget => _inCombat ? Mathf.Max(_zoomWanted, _combatZoom) : _zoomWanted;

	private void OnEnable()
	{
		//Captured once: were this taken again on a later enable it would read a zoomed rig as the authored one
		if (_authoredOrbits == null && freeLookVCam != null)
		{
			_authoredOrbits = new CinemachineFreeLook.Orbit[freeLookVCam.m_Orbits.Length];
			for (int i = 0; i < _authoredOrbits.Length; i++)
				_authoredOrbits[i] = freeLookVCam.m_Orbits[i]; //a struct, so this copies rather than aliases
		}

		inputReader.CameraZoomEvent += OnCameraZoom;
		inputReader.CameraMoveEvent += OnCameraMove;
		inputReader.EnableMouseControlCameraEvent += OnEnableMouseControlCamera;
		inputReader.DisableMouseControlCameraEvent += OnDisableMouseControlCamera;

		_protagonistTransformAnchor.OnAnchorProvided += SetupProtagonistVirtualCamera;
		_camShakeEvent.OnEventRaised += impulseSource.GenerateImpulse;

		if (_combatStateEvent != null)
			_combatStateEvent.OnEventRaised += OnCombatStateChanged;

		_cameraTransformAnchor.Provide(mainCamera.transform);
	}

	private void OnDisable()
	{
		inputReader.CameraZoomEvent -= OnCameraZoom;
		inputReader.CameraMoveEvent -= OnCameraMove;
		inputReader.EnableMouseControlCameraEvent -= OnEnableMouseControlCamera;
		inputReader.DisableMouseControlCameraEvent -= OnDisableMouseControlCamera;

		_protagonistTransformAnchor.OnAnchorProvided -= SetupProtagonistVirtualCamera;
		_camShakeEvent.OnEventRaised -= impulseSource.GenerateImpulse;

		if (_combatStateEvent != null)
			_combatStateEvent.OnEventRaised -= OnCombatStateChanged;

		_cameraTransformAnchor.Unset();
	}

	private void Start()
	{
		//Setup the camera target if the protagonist is already available
		if(_protagonistTransformAnchor.isSet)
			SetupProtagonistVirtualCamera();
	}

	private void OnEnableMouseControlCamera()
	{
		_isRMBPressed = true;

		Cursor.lockState = CursorLockMode.Locked;
		Cursor.visible = false;

		StartCoroutine(DisableMouseControlForFrame());
	}

	IEnumerator DisableMouseControlForFrame()
	{
		_cameraMovementLock = true;
		yield return new WaitForEndOfFrame();
		_cameraMovementLock = false;
	}

	private void OnDisableMouseControlCamera()
	{
		_isRMBPressed = false;

		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = true;

		// when mouse control is disabled, the input needs to be cleared
		// or the last frame's input will 'stick' until the action is invoked again
		freeLookVCam.m_XAxis.m_InputAxisValue = 0;
		freeLookVCam.m_YAxis.m_InputAxisValue = 0;
	}

	private void OnCameraMove(Vector2 cameraMovement, bool isDeviceMouse)
	{
		if (_cameraMovementLock)
			return;

		if (isDeviceMouse && !_isRMBPressed)
			return;

		//Using a "fixed delta time" if the device is mouse,
		//since for the mouse we don't have to account for frame duration
		float deviceMultiplier = isDeviceMouse ? 0.02f * _mouseSensitivity : Time.deltaTime;

		freeLookVCam.m_XAxis.m_InputAxisValue = cameraMovement.x * deviceMultiplier * _speedMultiplier;
		freeLookVCam.m_YAxis.m_InputAxisValue = cameraMovement.y * deviceMultiplier * _speedMultiplier;
	}

	/// <summary>
	/// Takes a turn of the wheel as a request to come nearer or pull back.
	/// </summary>
	/// <remarks>
	/// Only the distance asked for is recorded here. The camera walks towards it over the following frames,
	/// so a notch reads as a glide rather than a jump, and a viewer spinning the wheel is not fighting a
	/// camera that has already arrived.
	/// </remarks>
	private void OnCameraZoom(float scroll)
	{
		//Scrolling up asks to come closer, and closer is a smaller orbit
		_zoomWanted = Mathf.Clamp(_zoomWanted - scroll * _zoomSpeed, _minZoom, _maxZoom);
	}

	/// <summary>
	/// Stands the camera back while there is a fight on, and lets it come in again afterwards.
	/// </summary>
	/// <remarks>
	/// A fight at the authored distance is fought half off screen - the enemy circles out of frame and the
	/// swing lands on something the player cannot see. Standing back a little puts both of them in view.
	///
	/// The channel is raised by GameStateSO on the way into and out of combat, which is a state nothing was
	/// listening for until now.
	/// </remarks>
	private void OnCombatStateChanged(bool inCombat)
	{
		_inCombat = inCombat;
	}

	private void Update()
	{
		//Compared against where a fight says we should be, not against the wheel alone, or combat would set a
		//target the camera never travelled to
		if (_authoredOrbits == null || _zoom == ZoomTarget)
			return;

		float target = ZoomTarget;

		//Paced by an exponential rather than a flat share of the gap, so the glide takes the same time
		//whatever the frame rate
		_zoom = Mathf.Lerp(_zoom, target, 1f - Mathf.Exp(-_zoomSmoothing * Time.deltaTime));

		//Snapped once it is close enough to be indistinguishable, so this stops rewriting the rig every frame
		if (Mathf.Abs(target - _zoom) < .0005f)
			_zoom = target;

		int rigs = Mathf.Min(_authoredOrbits.Length, freeLookVCam.m_Orbits.Length);
		for (int i = 0; i < rigs; i++)
		{
			freeLookVCam.m_Orbits[i].m_Height = _authoredOrbits[i].m_Height * _zoom;
			freeLookVCam.m_Orbits[i].m_Radius = _authoredOrbits[i].m_Radius * _zoom;
		}
	}

	/// <summary>
	/// Provides Cinemachine with its target, taken from the TransformAnchor SO containing a reference to the player's Transform component.
	/// This method is called every time the player is reinstantiated.
	/// </summary>
	public void SetupProtagonistVirtualCamera()
	{
		Transform target = _protagonistTransformAnchor.Value;

		freeLookVCam.Follow = target;
		freeLookVCam.LookAt = target;
		freeLookVCam.OnTargetObjectWarped(target, target.position - freeLookVCam.transform.position - Vector3.forward);
	}
}
