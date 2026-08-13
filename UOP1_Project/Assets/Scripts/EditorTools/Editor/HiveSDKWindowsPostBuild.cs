using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// Lays out the native Hive SDK next to the built executable. The SDK drop ships the attribute
/// and context types for its own post-build step but not the step itself, so we do the copy
/// here. Layout mirrors a shipped Hive windows build: everything from 'additional' at the build
/// root, hive_string and hive_config.xml under 'resources'.
public class HiveSDKWindowsPostBuild : IPostprocessBuildWithReport
{
	private const string AdditionalPath = "Assets/Hive_SDK_v4/Plugins/Windows/additional";
	private const string HiveStringPath = "Assets/Hive_SDK_v4/Plugins/desktop/hive_string";
	private const string HiveConfigPath = "Assets/Plugins/Windows/res/hive_config.xml";

	public int callbackOrder => 0;

	public void OnPostprocessBuild(BuildReport report)
	{
		BuildTarget target = report.summary.platform;
		if (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.StandaloneWindows)
			return;

		string buildPath = Path.GetDirectoryName(report.summary.outputPath);
		if (string.IsNullOrEmpty(buildPath))
		{
			Debug.LogWarning("Hive SDK copy skipped: build output path is empty");
			return;
		}

		if (!Directory.Exists(AdditionalPath))
		{
			Debug.LogWarning("Hive SDK windows plugins not found: " + AdditionalPath);
			return;
		}

		Debug.Log("Copying Hive SDK (windows)");
		CopyDirectoryWithoutMeta(AdditionalPath, buildPath);

		//hive-sdk-res ships inside 'additional' but belongs under resources/
		string sdkResPath = buildPath + "/hive-sdk-res";
		if (Directory.Exists(sdkResPath))
		{
			CopyDirectoryWithoutMeta(sdkResPath, buildPath + "/resources");
			Directory.Delete(sdkResPath, true);
		}

		CopyDirectoryWithoutMeta(HiveStringPath, buildPath + "/resources/hive_string");

		if (File.Exists(HiveConfigPath))
		{
			File.Copy(HiveConfigPath, buildPath + "/resources/hive_config.xml", true);
			Debug.Log("  resources/hive_config.xml");
		}
		else
			Debug.LogWarning("Hive config not found: " + HiveConfigPath);

		Debug.Log("Hive SDK (windows) copy done.");
	}

	private static void CopyDirectoryWithoutMeta(string sourcePath, string destPath)
	{
		Directory.CreateDirectory(destPath);

		foreach (string file in Directory.GetFiles(sourcePath))
		{
			if (file.EndsWith(".meta"))
				continue;

			File.Copy(file, Path.Combine(destPath, Path.GetFileName(file)), true);
		}

		foreach (string dir in Directory.GetDirectories(sourcePath))
			CopyDirectoryWithoutMeta(dir, Path.Combine(destPath, Path.GetFileName(dir)));
	}
}
