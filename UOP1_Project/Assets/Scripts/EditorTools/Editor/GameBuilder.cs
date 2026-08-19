using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the addressable content and then the player, in one editor session.
/// </summary>
/// <remarks>
/// Driving these as two separate Unity invocations means starting the editor twice, and starting the editor
/// costs about fifteen seconds before it does any of the work asked of it - loading the project, refreshing
/// scripts, checking the licence. That is longer than an addressables build takes, and it was being paid
/// for what is really one build.
///
/// Both steps report through the exit code, which batchmode will not do on its own: it reports success
/// whatever happened, so a build that failed looks exactly like one that worked.
///
/// The output path can be given as -buildOutput &lt;path&gt; on the command line; without it the player goes
/// where it has always gone.
/// </remarks>
public static class GameBuilder
{
	private static readonly string[] DefaultOutput = { "Builds", "Windows64", "ChopChop.exe" };
	private const string OutputArgument = "-buildOutput";

	[MenuItem("Tools/Build Content and Player")]
	public static void BuildContentAndPlayer()
	{
		bool built = AddressablesContentBuilder.TryBuildContent() && TryBuildPlayer();

		if (Application.isBatchMode)
			EditorApplication.Exit(built ? 0 : 1);
	}

	/// <summary>
	/// Builds the player alone, for when only scripts have changed.
	/// </summary>
	/// <remarks>
	/// The bundles hold the scenes and their prefabs, so an asset edit needs the content build above. Code
	/// does not live in bundles, so a script edit does not.
	/// </remarks>
	[MenuItem("Tools/Build Player Only")]
	public static void BuildPlayerOnly()
	{
		bool built = TryBuildPlayer();

		if (Application.isBatchMode)
			EditorApplication.Exit(built ? 0 : 1);
	}

	private static bool TryBuildPlayer()
	{
		string output = ResolveOutput();

		//Only the scenes that are ticked, and in the order they are ticked in: the first is what the player
		//opens with
		string[] scenes = EditorBuildSettings.scenes
			.Where(s => s.enabled)
			.Select(s => s.path)
			.ToArray();

		if (scenes.Length == 0)
		{
			Debug.LogError("Player build: no scenes are enabled in the build settings");
			return false;
		}

		var options = new BuildPlayerOptions
		{
			scenes = scenes,
			locationPathName = output,
			target = BuildTarget.StandaloneWindows64,
			targetGroup = BuildTargetGroup.Standalone,
			options = BuildOptions.None,
		};

		BuildReport report = BuildPipeline.BuildPlayer(options);
		BuildSummary summary = report.summary;

		if (summary.result != BuildResult.Succeeded)
		{
			Debug.LogError("Player build " + summary.result + " with " + summary.totalErrors
						   + " error(s) after " + summary.totalTime.TotalSeconds.ToString("0.0") + "s");
			return false;
		}

		Debug.Log("Player build done in " + summary.totalTime.TotalSeconds.ToString("0.0") + "s -> " + output);
		return true;
	}

	private static string ResolveOutput()
	{
		string[] args = Environment.GetCommandLineArgs();

		for (int i = 0; i < args.Length - 1; i++)
		{
			if (args[i] == OutputArgument)
				return args[i + 1];
		}

		//Relative to the project rather than to wherever the editor was started from, which is not the same
		//place and has bitten a build before
		string project = Directory.GetParent(Application.dataPath).FullName;
		return Path.Combine(new[] { project }.Concat(DefaultOutput).ToArray());
	}
}
