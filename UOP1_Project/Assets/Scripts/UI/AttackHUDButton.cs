using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Puts a swing button in the corner of the HUD.
/// </summary>
/// <remarks>
/// The left button walks the character to wherever it is clicked, which left attacking on a key. That is
/// fine at a keyboard and no use at all to a remote viewer holding a phone, so the swing gets a button of
/// its own on screen.
///
/// Built here rather than laid out in the prefab because the HUD has no round button to copy the way the
/// inventory does, and a button drawn in code is one less thing to keep in step by hand.
/// </remarks>
public class AttackHUDButton : MonoBehaviour
{
	[SerializeField] private InputReader _inputReader = default;

	[Tooltip("The disc the button is drawn as")]
	[SerializeField] private Sprite _disc = default;

	[Tooltip("Picture laid on the disc")]
	[SerializeField] private Sprite _icon = default;

	//Big enough to hit with a thumb on a phone, which is the whole point of it
	private const float SIZE = 140f;

	//Clear of the corner, and clear of anything the streamer draws over it
	private const float MARGIN = 60f;

	//The picture is inset from the rim by a share of the button, so it holds whatever size the disc is
	private const float ICON_INSET = .22f;

	private bool _built;

	private void OnEnable()
	{
		//The HUD is switched off and on rather than rebuilt, so this only has to happen once
		if (_built)
			return;

		_built = true;
		Build();
	}

	private void Build()
	{
		var button = new GameObject("Attack Button", typeof(RectTransform), typeof(Image), typeof(Button));
		var rect = (RectTransform)button.transform;
		rect.SetParent(transform, false);

		//Pinned to the bottom right rather than stretched, so it keeps its size on any screen
		rect.anchorMin = new Vector2(1f, 0f);
		rect.anchorMax = new Vector2(1f, 0f);
		rect.pivot = new Vector2(1f, 0f);
		rect.sizeDelta = new Vector2(SIZE, SIZE);
		rect.anchoredPosition = new Vector2(-MARGIN, MARGIN);

		Image face = button.GetComponent<Image>();
		face.sprite = _disc;

		//Sliced, as the disc is drawn with a border meant to stretch
		if (_disc != null && _disc.border != Vector4.zero)
			face.type = Image.Type.Sliced;

		if (_icon != null)
			DrawIcon(rect);

		Button clickable = button.GetComponent<Button>();
		clickable.targetGraphic = face;
		clickable.onClick.AddListener(OnAttackPressed);
	}

	private void DrawIcon(RectTransform parent)
	{
		var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
		var rect = (RectTransform)icon.transform;
		rect.SetParent(parent, false);

		rect.anchorMin = new Vector2(ICON_INSET, ICON_INSET);
		rect.anchorMax = new Vector2(1f - ICON_INSET, 1f - ICON_INSET);
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;

		Image drawn = icon.GetComponent<Image>();
		drawn.sprite = _icon;

		//Not square where the box it is given is, and stretching it would show
		drawn.preserveAspect = true;

		//The button tints the disc underneath; staying off the raycast keeps the click on the button
		drawn.raycastTarget = false;
	}

	private void OnAttackPressed()
	{
		if (_inputReader != null)
			_inputReader.RequestAttack();
	}
}
