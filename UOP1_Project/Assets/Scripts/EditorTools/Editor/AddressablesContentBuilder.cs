using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// Rebuilds the addressable content, which a player build does not do on its own.
///
/// The game loads its scenes through Addressables, so those scenes and every prefab they pull in come
/// out of bundles rather than the player. Edit a prefab and build only the player, and the game keeps
/// loading whatever was in the bundles the last time this ran.
public static class AddressablesContentBuilder
{
	[MenuItem("Tools/Build Addressables Content")]
	public static void BuildContent()
	{
		bool built = TryBuildContent();

		//Batchmode reports success whatever happens, so the exit code has to be set here to be worth
		//anything to whatever is driving the build
		if (Application.isBatchMode)
			EditorApplication.Exit(built ? 0 : 1);
	}

	/// <summary>
	/// Rebuilds the content and says whether it worked, leaving the editor running.
	/// </summary>
	/// <remarks>
	/// Kept apart from the entry point above so a caller can go on to build the player in the same session.
	/// Starting the editor costs about fifteen seconds - loading the project, refreshing scripts, checking
	/// the licence - which is longer than this takes, and paying it twice for what is really one build was
	/// most of the wait.
	/// </remarks>
	public static bool TryBuildContent()
	{
		AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

		if (!string.IsNullOrEmpty(result.Error))
		{
			Debug.LogError("Addressables content build failed: " + result.Error);
			return false;
		}

		Debug.Log("Addressables content build done: " + result.LocationCount + " locations in "
				  + result.Duration.ToString("0.0") + "s -> " + result.OutputPath);
		return true;
	}
}
