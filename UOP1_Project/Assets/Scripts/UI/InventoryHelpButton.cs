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
			//The close button's X is a child of its own. Emptying the label was not enough to be rid of
			//it, so with an icon the face is cleared outright: the whole button is the picture, and its
			//size comes from its own rect rather than from anything underneath.
			for (int i = help.transform.childCount - 1; i >= 0; i--)
				Destroy(help.transform.GetChild(i).gameObject);

			DrawAsIcon(help);
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

		//The button tints its graphic as it is hovered and pressed, and puts the resting tint back
		//afterwards; left alone that would colour the icon rather than the disc it was picked for.
		if (_icon != null)
		{
			ColorBlock colors = button.colors;
			colors.normalColor = Color.white;
			button.colors = colors;
		}

		//The clone came with whatever closes the inventory wired up
		button.onClick.RemoveAllListeners();
		button.onClick.AddListener(OpenHelpPage);
	}

	/// Makes the button the icon, rather than laying the icon over the close button's red disc.
	private void DrawAsIcon(GameObject help)
	{
		//The button's own graphic is that disc, so replacing its sprite is what removes it
		Image face = help.GetComponent<Image>();
		if (face == null)
		{
			Debug.LogWarning("InventoryHelpButton: the cloned button has no Image to draw the icon on");
			return;
		}

		face.sprite = _icon;

		//The disc was drawn sliced to stretch its border; the icon is a plain picture and would be
		//pulled about by that, and it is not square once the button is
		face.type = Image.Type.Simple;
		face.preserveAspect = true;

		//The disc carried its red in the tint rather than the sprite
		face.color = Color.white;
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
