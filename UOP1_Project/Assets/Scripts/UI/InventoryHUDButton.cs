using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Opens the inventory when the bag on the HUD is clicked, next to the key the bag already advertises.
/// </summary>
/// <remarks>
/// The click is raised as the same event the key raises, so the screen opens through the one path in
/// UIManager. That path only opens the inventory while the game is being played, so clicking the bag
/// again once it is open does nothing.
/// </remarks>
public class InventoryHUDButton : MonoBehaviour, IPointerClickHandler
{
	[SerializeField] private InputReader _inputReader = default;

	public void OnPointerClick(PointerEventData eventData)
	{
		//The bag is a HUD hint rather than a full button, so only a plain left click counts
		if (eventData.button != PointerEventData.InputButton.Left)
			return;

		_inputReader.RequestOpenInventory();
	}
}
