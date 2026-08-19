using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum GameState
{
	Gameplay, //regular state: player moves, attacks, can perform actions
	Pause, //pause menu is opened, the whole game world is frozen
	Inventory, //when inventory UI or cooking UI are open
	Dialogue,
	Cutscene,
	LocationTransition, //when the character steps into LocationExit trigger, fade to black begins and control is removed from the player
	Combat, //enemy is nearby and alert, player can't open Inventory or initiate dialogues, but can pause the game
}

//[CreateAssetMenu(fileName = "GameState", menuName = "Gameplay/GameState", order = 51)]
public class GameStateSO : DescriptionBaseSO
{
	public GameState CurrentGameState => _currentGameState;
	
	[Header("Game states")]
	[SerializeField][ReadOnly] private GameState _currentGameState = default;
	[SerializeField][ReadOnly] private GameState _previousGameState = default;

	[Header("Broadcasting on")]
	[SerializeField] private BoolEventChannelSO _onCombatStateEvent = default;
	
	//Built here rather than in Start, which Unity never calls on a ScriptableObject: the list stayed null
	//and every call below threw on its first line, which took Combat with it - the only way into that state
	//sits after the throw in AddAlertEnemy.
	//Emptied on load as well, because a ScriptableObject keeps its runtime state between play sessions in
	//the editor, and enemies left over from the last run would go on counting as alert.
	private List<Transform> _alertEnemies = new List<Transform>();

	private void OnEnable()
	{
		_alertEnemies.Clear();

		//A critter that goes down with its scene never gets to say so, so the unload is the cue to check
		SceneManager.sceneUnloaded += OnSceneUnloaded;
	}

	private void OnDisable()
	{
		SceneManager.sceneUnloaded -= OnSceneUnloaded;
	}

	public void AddAlertEnemy(Transform enemy)
	{
		if (!_alertEnemies.Contains(enemy))
		{
			_alertEnemies.Add(enemy);
		}

		UpdateGameState(GameState.Combat);
	}

	public void RemoveAlertEnemy(Transform enemy)
	{
		bool wasAlert = _alertEnemies.Remove(enemy);

		//Whatever is no longer there is dropped at the same time, so one of those cannot hold the count off
		//zero and keep the game in combat with nothing left to fight
		bool droppedGone = DropDestroyedEnemies();

		if (wasAlert || droppedGone)
			LeaveCombatIfNothingIsAlert();
	}

	/// <summary>
	/// Forgets any alert enemy that no longer exists.
	/// </summary>
	/// <remarks>
	/// A critter that dies goes through its dying state, which takes it off this list on the way past. One
	/// destroyed without ever entering that state - carried off with its scene when the player walks out of
	/// the location - never does, and what it leaves behind is an entry that can never be matched or removed
	/// by name again. The count then never comes back to zero and combat never ends.
	/// </remarks>
	private bool DropDestroyedEnemies()
	{
		//Being equal to null is the only trace a destroyed object leaves
		return _alertEnemies.RemoveAll(enemy => enemy == null) > 0;
	}

	/// <summary>
	/// Returns to ordinary play once nothing is alert, and only from combat.
	/// </summary>
	/// <remarks>
	/// Anything else the game is in the middle of - a transition, a cutscene, a conversation - is not ours to
	/// cut short just because the last enemy stopped being interested.
	/// </remarks>
	private void LeaveCombatIfNothingIsAlert()
	{
		if (_alertEnemies.Count == 0 && _currentGameState == GameState.Combat)
			UpdateGameState(GameState.Gameplay);
	}

	private void OnSceneUnloaded(Scene scene)
	{
		//Runs once the scene's objects are already gone, which is what makes them recognisable as such here
		if (DropDestroyedEnemies())
			LeaveCombatIfNothingIsAlert();
	}

	public void UpdateGameState(GameState newGameState)
	{
		if (newGameState == CurrentGameState)
			return;

		if (newGameState == GameState.Combat)
		{
			_onCombatStateEvent.RaiseEvent(true);
		}
		else
		{
			_onCombatStateEvent.RaiseEvent(false);
		}

		_previousGameState = _currentGameState;
		_currentGameState = newGameState;
	}

	public void ResetToPreviousGameState()
	{
		if (_previousGameState == _currentGameState)
			return;

		if (_previousGameState == GameState.Combat)
		{
			_onCombatStateEvent.RaiseEvent(false);
		}
		else if (_currentGameState == GameState.Combat)
		{
			_onCombatStateEvent.RaiseEvent(true);
		}
		
		GameState stateToReturnTo = _previousGameState;
		_previousGameState = _currentGameState;
		_currentGameState = stateToReturnTo;
	}
}
