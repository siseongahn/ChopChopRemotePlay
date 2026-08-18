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
		AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

		bool failed = !string.IsNullOrEmpty(result.Error);

		if (failed)
			Debug.LogError("Addressables content build failed: " + result.Error);
		else
			Debug.Log("Addressables content build done: " + result.LocationCount + " locations in "
					  + result.Duration.ToString("0.0") + "s -> " + result.OutputPath);

		//Batchmode reports success whatever happens, so the exit code has to be set here to be worth
		//anything to whatever is driving the build
		if (Application.isBatchMode)
			EditorApplication.Exit(failed ? 1 : 0);
	}
}
