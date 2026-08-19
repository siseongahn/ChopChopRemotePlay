using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using hive;
#endif

/// <summary>
/// Puts a help button next to the inventory's close button, which opens a page in Hive's in-app
/// browser.
/// </summary>
/// <remarks>
/// The button is cloned from the close button rather than laid out here, so it keeps that button's
/// size and look for free and follows it if the shared button prefab is ever restyled. Only the label,
/// the position and what the click does are ours.
///
/// The in-app browser is CEF, which the SDK only ships for windows players, so elsewhere the click
/// just says what it would have opened.
/// </remarks>
public class InventoryHelpButton : MonoBehaviour
{
	[Tooltip("Page to open in the in-app browser")]
	[SerializeField] private string _url = "https://www.youtube.com";
	[Tooltip("Icon the button is drawn as")]
	[SerializeField] private Sprite _icon = default;
	[Tooltip("Text to fall back on if no icon is set")]
	[SerializeField] private string _label = "?";
	[Tooltip("Name of the button to sit next to and copy")]
	[SerializeField] private string _closeButtonName = "Close Button";
	[Tooltip("Gap between the two buttons, in pixels")]
	[SerializeField] private float _gap = 8f;

	private bool _built;

	private void OnEnable()
	{
		//The inventory is switched off and on rather than rebuilt, so this only has to happen once
		if (_built)
			return;

		Build();
	}

	private void Build()
	{
		Transform close = transform.Find(_closeButtonName);
		if (close == null)
		{
			Debug.LogWarning("InventoryHelpButton: no '" + _closeButtonName + "' to sit next to");
			return;
		}

		_built = true;

		GameObject help = Instantiate(close.gameObject, transform);
		help.name = "Help Button";

		//The shared button takes its label from the string tables, which would put the close button's
		//text straight back over ours. Matched by name so this does not have to reference Localization.
		foreach (MonoBehaviour behaviour in help.GetComponentsInChildren<MonoBehaviour>(true))
		{
			if (behaviour.GetType().Name == "LocalizeStringEvent")
				Destroy(behaviour);
		}

		if (_icon != null)
		{
			//The close button's X is a child of its own, and emptying the label was not enough to be rid of
			//it, so the face is cleared and our picture put there instead. The disc the button is drawn as
			//stays: this is the same round button as the one beside it, wearing a different face.
			for (int i = help.transform.childCount - 1; i >= 0; i--)
				Destroy(help.transform.GetChild(i).gameObject);

			DrawIconOnDisc(help);
		}
		else
		{
			foreach (TMP_Text text in help.GetComponentsInChildren<TMP_Text>(true))
				text.text = _label;
		}

		//Immediately before the close button. Both are pinned to the same corner and do not stretch, so
		//sizeDelta is the real width even before the layout has run.
		var closeRect = (RectTransform)close;
		var helpRect = (RectTransform)help.transform;
		helpRect.anchoredPosition = closeRect.anchoredPosition
									- new Vector2(closeRect.sizeDelta.x + _gap, 0f);
		helpRect.SetSiblingIndex(close.GetSiblingIndex());

		Button button = help.GetComponent<Button>();
		if (button == null)
		{
			Debug.LogWarning("InventoryHelpButton: the cloned button has no Button on it");
			return;
		}

		//The clone came with whatever closes the inventory wired up
		button.onClick.RemoveAllListeners();
		button.onClick.AddListener(OpenHelpPage);
	}

	/// Lays the icon on the button's disc, leaving the disc itself alone.
	private void DrawIconOnDisc(GameObject help)
	{
		//RectTransform asked for outright: a new GameObject comes with a plain Transform, and the cast below
		//has to be sound
		var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
		var rect = (RectTransform)icon.transform;
		rect.SetParent(help.transform, false);

		//Inset from the rim by a share of the button rather than a count of pixels, so the picture sits
		//within the disc at whatever size the shared button is styled to.
		const float inset = .18f;
		rect.anchorMin = new Vector2(inset, inset);
		rect.anchorMax = new Vector2(1f - inset, 1f - inset);
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;

		Image drawn = icon.GetComponent<Image>();
		drawn.sprite = _icon;

		//The icon is not square where the box it is given is, and stretching it would be obvious
		drawn.preserveAspect = true;

		//The button tints its own graphic, which is the disc underneath. Staying off the raycast keeps the
		//click on the button rather than on the picture sitting over it.
		drawn.raycastTarget = false;
	}

	private void OpenHelpPage()
	{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
		Debug.Log("InventoryHelpButton: opening " + _url);

		InAppWebViewParam param = new InAppWebViewParam.Builder(_url).build();
		PlatformHelper.showInAppWebView(param, result =>
			Debug.Log("InventoryHelpButton: in-app webview result " + result.toString()));
#else
		Debug.Log("InventoryHelpButton: the in-app browser is windows-player only; would have opened " + _url);
#endif
	}
}
