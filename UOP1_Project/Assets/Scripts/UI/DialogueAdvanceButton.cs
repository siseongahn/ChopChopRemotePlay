using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Lets a click on the dialogue box move the dialogue on, as the key does.
/// </summary>
/// <remarks>
/// This sits on the panel itself rather than on any one part of it, and relies on a click bubbling up
/// from whatever was actually under the pointer. That is what keeps the choices working: a click on a
/// choice is answered by that button and stops there, so it picks the choice instead of also skipping
/// past it. Anywhere else on the box - the text, the name plate, the background - has nothing of its
/// own to answer with, so the click arrives here.
///
/// The panel is only on screen while somebody is talking, so there is nothing to click otherwise.
/// </remarks>
public class DialogueAdvanceButton : MonoBehaviour, IPointerClickHandler
{
	[SerializeField] private InputReader _inputReader = default;

	public void OnPointerClick(PointerEventData eventData)
	{
		//Advancing is a plain left click, the same as the key is a plain press
		if (eventData.button != PointerEventData.InputButton.Left)
			return;

		_inputReader.RequestAdvanceDialogue();
	}
}
