using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.HiveEditor;
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

		RunHivePluginPostBuildSteps(report.summary, buildPath);
		CopyRemotePlayHost(buildPath);
	}

	/// Calls the post-build steps that Hive plugins mark with [HivePostBuild].
	///
	/// The SDK ships the attribute and the context struct but nothing that looks for them, so a plugin
	/// carrying such a step compiles and then never runs - which is why the RemotePlay binaries had to be
	/// placed in the build by hand before this existed. Ordering is the attribute's own.
	private static void RunHivePluginPostBuildSteps(BuildSummary summary, string buildPath)
	{
		//RemotePlay's step puts RemotePlayDll.dll at the root of this and the rest under RemotePlay/, which
		//is the layout a working build has
		string pluginDeployPath = Path.Combine(buildPath, "plugins");
		Directory.CreateDirectory(pluginDeployPath);

		var context = new HivePostBuildContext(summary, pluginDeployPath);

		var steps = new List<MethodInfo>();
		foreach (MethodInfo method in TypeCache.GetMethodsWithAttribute<HivePostBuildAttribute>())
		{
			ParameterInfo[] parameters = method.GetParameters();
			if (!method.IsStatic || parameters.Length != 1 || parameters[0].ParameterType != typeof(HivePostBuildContext))
			{
				Debug.LogWarning("Hive post-build: skipping " + method.DeclaringType?.Name + "." + method.Name
								 + ", it does not take a single HivePostBuildContext");
				continue;
			}

			steps.Add(method);
		}

		steps.Sort((a, b) => a.GetCustomAttribute<HivePostBuildAttribute>().Order
							 .CompareTo(b.GetCustomAttribute<HivePostBuildAttribute>().Order));

		foreach (MethodInfo step in steps)
		{
			//One plugin throwing is not a reason to lose the rest of the build
			try
			{
				step.Invoke(null, new object[] { context });
				Debug.Log("Hive post-build: ran " + step.DeclaringType?.Name + "." + step.Name);
			}
			catch (Exception e)
			{
				Debug.LogError("Hive post-build: " + step.DeclaringType?.Name + "." + step.Name + " failed: "
							   + (e.InnerException ?? e));
			}
		}
	}

	/// Lays down the RemotePlay host, which RemotePlay's own step does not copy.
	///
	/// The 1.02.00 package ships HiveRemoteHost.exe but its post-build step lists everything except that,
	/// so a build assembled purely by the vendor's script comes out without the host process it needs.
	private static void CopyRemotePlayHost(string buildPath)
	{
		const string source = "Assets/HiveRemotePlay/Plugins/windows/HiveRemoteHost.exe";
		if (!File.Exists(source))
			return;

		string destination = Path.Combine(buildPath, "plugins", "RemotePlay");
		Directory.CreateDirectory(destination);
		File.Copy(source, Path.Combine(destination, "HiveRemoteHost.exe"), true);

		Debug.Log("Hive post-build: copied HiveRemoteHost.exe, which RemotePlay's own step leaves out");
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
