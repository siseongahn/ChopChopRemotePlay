using System.Collections.Generic;
using UnityEngine;

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
		if (_alertEnemies.Contains(enemy))
		{
			_alertEnemies.Remove(enemy);

			if (_alertEnemies.Count == 0)
			{
				UpdateGameState(GameState.Gameplay);
			}
		}
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
