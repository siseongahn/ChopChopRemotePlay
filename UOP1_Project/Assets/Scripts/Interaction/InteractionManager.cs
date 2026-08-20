using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

public enum InteractionType { None = 0, PickUp, Cook, Talk };

public class InteractionManager : MonoBehaviour
{
	[SerializeField] private InputReader _inputReader = default;

	//Events for the different interaction types
	[Header("Broadcasting on")]
	[SerializeField] private ItemEventChannelSO _onObjectPickUp = default;
	[SerializeField] private VoidEventChannelSO _onCookingStart = default;
	[SerializeField] private DialogueActorChannelSO _startTalking = default;
	[SerializeField] private InteractionUIEventChannelSO _toggleInteractionUI = default;

	[Header("Listening to")]
	[SerializeField] private VoidEventChannelSO _onInteractionEnded = default;
	[SerializeField] private PlayableDirectorChannelSO _onCutsceneStart = default;
	
	[ReadOnly] public InteractionType currentInteractionType; //This is checked/consumed by conditions in the StateMachine

	private LinkedList<Interaction> _potentialInteractions = new LinkedList<Interaction>(); //To store the objects we the player could potentially interact with

	private Protagonist _protagonist;

	private void Awake()
	{
		_protagonist = GetComponent<Protagonist>();
	}

	private void OnEnable()
	{
		_inputReader.InteractEvent += OnInteractionButtonPress;
		_onInteractionEnded.OnEventRaised += OnInteractionEnd;
		_onCutsceneStart.OnEventRaised += ResetPotentialInteractions;
	}

	private void OnDisable()
	{
		_inputReader.InteractEvent -= OnInteractionButtonPress;
		_onInteractionEnded.OnEventRaised -= OnInteractionEnd;
		_onCutsceneStart.OnEventRaised -= ResetPotentialInteractions;
	}

	// Called mid-way through the AnimationClip of collecting
	private void Collect()
	{
		GameObject itemObject = _potentialInteractions.First.Value.interactableObject;
		_potentialInteractions.RemoveFirst();

		if (_onObjectPickUp != null)
		{
			ItemSO currentItem = itemObject.GetComponent<CollectableItem>().GetItem();
			_onObjectPickUp.RaiseEvent(currentItem);
		}

		Destroy(itemObject); //TODO: maybe move this destruction in a more general manger, to implement a removal SFX

		RequestUpdateUI(false);
	}

	/// <summary>
	/// Puts the nearest thing in range at the front of the list, which is the end everything else reads.
	/// </summary>
	/// <remarks>
	/// The list is ordered by when things came into range. That was fair enough while the detector was a
	/// narrow slab held out in front, where the latest arrival was whatever the player had just walked up to.
	/// Now that it reaches to the sides and a little behind, several things are usually in range at once and
	/// the latest arrival is as likely to be the one over the player's shoulder as the one under their nose.
	/// </remarks>
	private void PromoteNearest()
	{
		if (_potentialInteractions.Count < 2)
			return;

		LinkedListNode<Interaction> nearest = null;
		float nearestDistance = float.MaxValue;

		for (LinkedListNode<Interaction> node = _potentialInteractions.First; node != null; node = node.Next)
		{
			GameObject candidate = node.Value.interactableObject;
			if (candidate == null)
				continue;

			//Squared, because only the ordering matters here
			float distance = (candidate.transform.position - transform.position).sqrMagnitude;
			if (distance < nearestDistance)
			{
				nearestDistance = distance;
				nearest = node;
			}
		}

		if (nearest == null || nearest == _potentialInteractions.First)
			return;

		//Read before the node leaves the list, rather than trusting a removed node to still hold its value
		Interaction winner = nearest.Value;
		_potentialInteractions.Remove(nearest);
		_potentialInteractions.AddFirst(winner);
	}

	private void OnInteractionButtonPress()
	{
		if (_potentialInteractions.Count == 0)
			return;

		//Whatever is nearest now, rather than whatever was walked into last
		PromoteNearest();

		//Interacting wins over swinging. The StateMachine looks at attacking before it looks at picking
		//something up, so a swing that is waiting to be spent takes the turn and the item stays on the
		//ground. Talking and cooking escape that by moving the input off the gameplay map below; picking up
		//has nothing to move, so the cached swing is taken back instead.
		//This mattered most while the left button both attacked and worked the HUD, which it no longer does,
		//but the ordering is the StateMachine's and holds for a swing asked for any other way too.
		if (_protagonist != null)
			_protagonist.ConsumeAttackInput();

		currentInteractionType = _potentialInteractions.First.Value.type;

		switch (_potentialInteractions.First.Value.type)
		{
			case InteractionType.Cook:
				if (_onCookingStart != null)
				{
					_onCookingStart.RaiseEvent();
					_inputReader.EnableMenuInput();
				}
				break;

			case InteractionType.Talk:
				if (_startTalking != null)
				{
					_potentialInteractions.First.Value.interactableObject.GetComponent<StepController>().InteractWithCharacter();
					_inputReader.EnableDialogueInput();
				}
				break;

				//No need to do anything for Pickup type, the StateMachine will transition to the state
				//and then the AnimationClip will call Collect()
		}
	}

	//Called by the Event on the trigger collider on the child GO called "InteractionDetector"
	public void OnTriggerChangeDetected(bool entered, GameObject obj)
	{
		if (entered)
			AddPotentialInteraction(obj);
		else
			RemovePotentialInteraction(obj);
	}

	private void AddPotentialInteraction(GameObject obj)
	{
		Interaction newPotentialInteraction = new Interaction(InteractionType.None, obj);

		if (obj.CompareTag("Pickable"))
		{
			newPotentialInteraction.type = InteractionType.PickUp;
		}
		else if (obj.CompareTag("CookingPot"))
		{
			newPotentialInteraction.type = InteractionType.Cook;
		}
		else if (obj.CompareTag("NPC"))
		{
			newPotentialInteraction.type = InteractionType.Talk;
		}

		if (newPotentialInteraction.type != InteractionType.None)
		{
			_potentialInteractions.AddFirst(newPotentialInteraction);
			RequestUpdateUI(true);
		}
	}

	private void RemovePotentialInteraction(GameObject obj)
	{
		LinkedListNode<Interaction> currentNode = _potentialInteractions.First;
		while (currentNode != null)
		{
			if (currentNode.Value.interactableObject == obj)
			{
				_potentialInteractions.Remove(currentNode);
				break;
			}
			currentNode = currentNode.Next;
		}

		RequestUpdateUI(_potentialInteractions.Count > 0);
	}

	private void RequestUpdateUI(bool visible)
	{
		//Asking to show it is not the same as having something to show. Cooking and talking ask for the prompt
		//back when they finish, and by then the player may have walked away from the pot or the person they
		//were busy with, leaving nothing in range to name - so this read the type off an empty list.
		if (visible && _potentialInteractions.Count > 0)
		{
			//So the prompt names the same thing the button would act on
			PromoteNearest();
			_toggleInteractionUI.RaiseEvent(true, _potentialInteractions.First.Value.type);
		}
		else
			_toggleInteractionUI.RaiseEvent(false, InteractionType.None);
	}

	private void OnInteractionEnd()
	{
		switch (currentInteractionType)
		{
			case InteractionType.Cook:
			case InteractionType.Talk:
				//We show the UI after cooking or talking, in case player wants to interact again
				RequestUpdateUI(true);
				break;
		}

		_inputReader.EnableGameplayInput();
	}

	private void ResetPotentialInteractions(PlayableDirector _playableDirector)
	{
		_potentialInteractions.Clear();
		RequestUpdateUI(_potentialInteractions.Count > 0);
	}
}
