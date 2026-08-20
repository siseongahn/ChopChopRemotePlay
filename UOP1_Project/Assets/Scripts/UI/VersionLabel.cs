using TMPro;
using UnityEngine;

/// <summary>
/// Writes the build's own version into the label it sits on.
/// </summary>
/// <remarks>
/// The number used to be typed into the canvas, which meant it lived in two places that had no way of
/// disagreeing loudly: the project said 1.0 while the menu said v 0.9, and each release since has needed
/// both changed by hand. Application.version reads the one in the player settings, so that becomes the only
/// place to change.
///
/// No format field on purpose. A string field the bundled prefab has never heard of deserializes to nothing
/// rather than to whatever the code initialises it with, so the label would come up empty in a build while
/// looking right in the editor - a trap this project has fallen into before with new inspector fields.
/// </remarks>
[RequireComponent(typeof(TMP_Text))]
public class VersionLabel : MonoBehaviour
{
	private void Awake()
	{
		TMP_Text label = GetComponent<TMP_Text>();
		if (label == null)
		{
			Debug.LogWarning("VersionLabel: nothing here to write the version into");
			return;
		}

		label.text = "v " + Application.version;
	}
}
