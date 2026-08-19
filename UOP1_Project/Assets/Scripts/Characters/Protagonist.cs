using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// <para>This component consumes input on the InputReader and stores its values. The input is then read, and manipulated, by the StateMachines's Actions.</para>
/// </summary>
public class Protagonist : MonoBehaviour
{
	[SerializeField] private InputReader _inputReader = default;
	[SerializeField] private TransformAnchor _gameplayCameraTransform = default;

	private Vector2 _inputVector;
	private float _previousSpeed;

	//Where a click asked us to walk to, and the stick input made up to get there. Held apart from
	//_inputVector so a hand on the keys can be told from the walk steering itself.
	private Vector3? _destination;
	private Vector2 _autoInput;
	private Camera _clickCamera;

	//Close enough to call it arrived. Short, but not so short that the character shuffles about on the spot
	//hunting for a point it cannot stand exactly on.
	private const float ARRIVAL_DISTANCE = .3f;

	//Eased off over this last stretch, so the walk settles rather than stopping dead
	private const float SLOWING_DISTANCE = 1.2f;

	//A click further away than this is taken as a misclick on the horizon rather than somewhere to walk
	private const float MAX_CLICK_DISTANCE = 200f;

	//Given up on if the ground gets no nearer for this long, which is what a wall in the way looks like from
	//here: the steering is a straight line and knows nothing about going around anything.
	private const float STALL_TIMEOUT = 1f;
	private const float STALL_PROGRESS = .05f;

	private float _stallTimer;
	private float _closestApproach;

	//These fields are read and manipulated by the StateMachine actions
	[NonSerialized] public bool jumpInput;
	[NonSerialized] public bool extraActionInput;
	[NonSerialized] public bool attackInput;
	[NonSerialized] public Vector3 movementInput; //Initial input coming from the Protagonist script
	[NonSerialized] public Vector3 movementVector; //Final movement vector, manipulated by the StateMachine actions
	[NonSerialized] public ControllerColliderHit lastHit;
	[NonSerialized] public bool isRunning; // Used when using the keyboard to run, brings the normalised speed to 1

	public const float GRAVITY_MULTIPLIER = 5f;
	public const float MAX_FALL_SPEED = -50f;
	public const float MAX_RISE_SPEED = 100f;
	public const float GRAVITY_COMEBACK_MULTIPLIER = .03f;
	public const float GRAVITY_DIVIDER = .6f;
	public const float AIR_RESISTANCE = 5f;

	private void OnControllerColliderHit(ControllerColliderHit hit)
	{
		lastHit = hit;
	}

	//Adds listeners for events being triggered in the InputReader script
	private void OnEnable()
	{
		_inputReader.JumpEvent += OnJumpInitiated;
		_inputReader.JumpCanceledEvent += OnJumpCanceled;
		_inputReader.MoveEvent += OnMove;
		_inputReader.StartedRunning += OnStartedRunning;
		_inputReader.StoppedRunning += OnStoppedRunning;
		_inputReader.AttackEvent += OnStartedAttack;
		_inputReader.PointerClickEvent += OnPointerClick;
		//...
	}

	//Removes all listeners to the events coming from the InputReader script
	private void OnDisable()
	{
		_inputReader.JumpEvent -= OnJumpInitiated;
		_inputReader.JumpCanceledEvent -= OnJumpCanceled;
		_inputReader.MoveEvent -= OnMove;
		_inputReader.StartedRunning -= OnStartedRunning;
		_inputReader.StoppedRunning -= OnStoppedRunning;
		_inputReader.AttackEvent -= OnStartedAttack;
		_inputReader.PointerClickEvent -= OnPointerClick;

		//Nothing should be steering us while this is switched off
		StopWalking();
		//...
	}

	private void Update()
	{
		SteerTowardsDestination();
		RecalculateMovement();
	}

	private void RecalculateMovement()
	{
		float targetSpeed;
		Vector3 adjustedMovement;

		//A hand on the keys or the stick always wins; the walk is only ever what fills the gap
		Vector2 steering = _inputVector.sqrMagnitude > 0f ? _inputVector : _autoInput;

		if (_gameplayCameraTransform.isSet)
		{
			//Get the two axes from the camera and flatten them on the XZ plane
			Vector3 cameraForward = _gameplayCameraTransform.Value.forward;
			cameraForward.y = 0f;
			Vector3 cameraRight = _gameplayCameraTransform.Value.right;
			cameraRight.y = 0f;

			//Use the two axes, modulated by the corresponding inputs, and construct the final vector
			adjustedMovement = cameraRight.normalized * steering.x +
				cameraForward.normalized * steering.y;
		}
		else
		{
			//No CameraManager exists in the scene, so the input is just used absolute in world-space
			Debug.LogWarning("No gameplay camera in the scene. Movement orientation will not be correct.");
			adjustedMovement = new Vector3(steering.x, 0f, steering.y);
		}

		//Fix to avoid getting a Vector3.zero vector, which would result in the player turning to x:0, z:0
		if (steering.sqrMagnitude == 0f)
			adjustedMovement = transform.forward * (adjustedMovement.magnitude + .01f);

		//Accelerate/decelerate
		targetSpeed = Mathf.Clamp01(steering.magnitude);
		if (targetSpeed > 0f)
		{
			// This is used to set the speed to the maximum if holding the Shift key,
			// to allow keyboard players to "run"
			if (isRunning)
				targetSpeed = 1f;

			if (attackInput)
				targetSpeed = .05f;
		}
		targetSpeed = Mathf.Lerp(_previousSpeed, targetSpeed, Time.deltaTime * 4f);

		movementInput = adjustedMovement.normalized * targetSpeed;

		_previousSpeed = targetSpeed;
	}

	/// <summary>
	/// Turns a place to walk to into the stick input that would take us there.
	/// </summary>
	/// <remarks>
	/// Made up as input rather than moved directly, so the walk goes through everything a held key already
	/// goes through: the same turning, the same acceleration, the same animation, the same states. It steers
	/// in a straight line and knows nothing of paths, so it gives up when it stops making headway.
	/// </remarks>
	private void SteerTowardsDestination()
	{
		if (!_destination.HasValue)
			return;

		//Taking hold of the keys or the stick calls the walk off, so the player is never fighting it
		if (_inputVector.sqrMagnitude > 0f)
		{
			StopWalking();
			return;
		}

		Vector3 toDestination = _destination.Value - transform.position;

		//Flattened: how far up or down the ground happens to be is not something to walk towards
		toDestination.y = 0f;
		float distance = toDestination.magnitude;

		if (distance <= ARRIVAL_DISTANCE)
		{
			StopWalking();
			return;
		}

		//Headway is measured against the closest we have ever been, so drifting sideways along a wall does
		//not read as progress
		if (distance < _closestApproach - STALL_PROGRESS)
		{
			_closestApproach = distance;
			_stallTimer = 0f;
		}
		else
		{
			_stallTimer += Time.deltaTime;
			if (_stallTimer >= STALL_TIMEOUT)
			{
				StopWalking();
				return;
			}
		}

		//Eased off over the last stretch so the character settles onto the spot
		float speed = Mathf.Clamp01(distance / SLOWING_DISTANCE);
		Vector3 direction = toDestination / distance;

		if (_gameplayCameraTransform.isSet)
		{
			//Undoing what RecalculateMovement is about to do: it reads the stick against the camera's axes,
			//so the direction has to be given back in those terms to come out pointing where we meant.
			Vector3 cameraForward = _gameplayCameraTransform.Value.forward;
			cameraForward.y = 0f;
			Vector3 cameraRight = _gameplayCameraTransform.Value.right;
			cameraRight.y = 0f;

			_autoInput = new Vector2(Vector3.Dot(direction, cameraRight.normalized),
									 Vector3.Dot(direction, cameraForward.normalized)) * speed;
		}
		else
		{
			_autoInput = new Vector2(direction.x, direction.z) * speed;
		}
	}

	/// <summary>
	/// Sends the character walking to a point on the ground.
	/// </summary>
	public void WalkTo(Vector3 point)
	{
		_destination = point;

		//Started at infinity so the first frame always counts as headway
		_closestApproach = float.MaxValue;
		_stallTimer = 0f;
	}

	public void StopWalking()
	{
		_destination = null;
		_autoInput = Vector2.zero;
	}

	private void OnPointerClick(Vector2 screenPosition)
	{
		//A click on the HUD is aimed at the HUD. The remote router hands its own taps straight to the UI and
		//does not press this button for them, but a mouse on the machine presses it wherever it is pointing.
		if (IsOverUI(screenPosition))
			return;

		if (TryGroundPointUnder(screenPosition, out Vector3 point))
			WalkTo(point);
	}

	private bool TryGroundPointUnder(Vector2 screenPosition, out Vector3 point)
	{
		point = default;

		//Looked up late and kept: the camera arrives with its own prefab, so it is not there to be found when
		//this component wakes
		if (_clickCamera == null)
			_clickCamera = Camera.main;

		if (_clickCamera == null)
			return false;

		Ray ray = _clickCamera.ScreenPointToRay(screenPosition);

		//Triggers ignored: the interaction volumes and the hurtboxes hang in the air around things and would
		//catch the click well short of the ground
		if (!Physics.Raycast(ray, out RaycastHit hit, MAX_CLICK_DISTANCE, ~0, QueryTriggerInteraction.Ignore))
			return false;

		//Our own collider fills the middle of the screen; walking to where we already are is not a move
		if (hit.collider.transform.IsChildOf(transform))
			return false;

		point = hit.point;
		return true;
	}

	private static bool IsOverUI(Vector2 screenPosition)
	{
		EventSystem events = EventSystem.current;
		if (events == null)
			return false;

		var data = new PointerEventData(events) { position = screenPosition };
		var hits = new List<RaycastResult>();
		events.RaycastAll(data, hits);

		return hits.Count > 0;
	}

	//---- EVENT LISTENERS ----

	private void OnMove(Vector2 movement)
	{

		_inputVector = movement;
	}

	private void OnJumpInitiated()
	{
		jumpInput = true;
	}

	private void OnJumpCanceled()
	{
		jumpInput = false;
	}

	private void OnStoppedRunning() => isRunning = false;

	private void OnStartedRunning() => isRunning = true;


	private void OnStartedAttack() => attackInput = true;

	// Triggered from Animation Event
	public void ConsumeAttackInput() => attackInput = false;
}
