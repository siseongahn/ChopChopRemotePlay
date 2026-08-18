using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Makes the interaction prompt on the HUD do what its key does when it is clicked or tapped.
/// </summary>
/// <remarks>
/// The click is raised as the same event the key raises, so what it turns into - talking to somebody,
/// cooking, picking something up - is decided by whatever is in reach rather than here. The prompt is
/// only on screen while there is something to interact with, so there is nothing to press otherwise.
/// </remarks>
public class InteractionHUDButton : MonoBehaviour, IPointerClickHandler
{
	[SerializeField] private InputReader _inputReader = default;

	public void OnPointerClick(PointerEventData eventData)
	{
		//The prompt is a HUD hint rather than a full button, so only a plain left click counts
		if (eventData.button != PointerEventData.InputButton.Left)
			return;

		_inputReader.RequestInteract();
	}
}
