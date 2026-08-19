using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Makes a TextMeshPro font asset out of a Korean font and hangs it off the fonts already in use as a
/// fallback, so Korean text draws without touching a single text component.
/// </summary>
/// <remarks>
/// The fonts this game ships with cover Latin and nothing else - the dialogue font carries 209 glyphs - so
/// Korean text comes out as empty boxes until something in the chain knows how to draw it.
///
/// The atlas is dynamic rather than baked. Korean needs 2,350 syllables for everyday text and 11,172 for
/// all of them, and baking that many into a static atlas is both a large asset and a guess about which
/// ones the game will ever say. Dynamic means glyphs are rasterised from the font as lines actually appear.
///
/// Registered as a fallback rather than assigned anywhere: a fallback is consulted only for characters the
/// first font hasn't got, so English keeps the look it was designed with and Korean borrows this one.
/// </remarks>
public static class KoreanFontBuilder
{
	//Bold first because the font it stands in for is a bold one, and Korean drawn at a lighter weight beside
	//it reads as a different voice. The rest are here so swapping the file is all it takes.
	private static readonly string[] CandidateFonts =
	{
		"Assets/Art/Font/NotoSansKR/NotoSansKR-Bold.ttf",
		"Assets/Art/Font/NotoSansKR/NotoSansKR-Regular.ttf",
		"Assets/Art/Font/NanumGothic/NanumGothic-Regular.ttf",
	};

	private const string FontAssetPath = "Assets/Art/Font/KoreanFallback SDF.asset";

	//Sampled once at this size and scaled by the renderer, which is what makes one atlas serve every text
	//size in the game
	private const int SamplingPointSize = 48;
	private const int AtlasPadding = 5;
	private const int AtlasSize = 1024;

	[MenuItem("Tools/Build Korean Font Fallback")]
	public static void Build()
	{
		string source = CandidateFonts.FirstOrDefault(p => File.Exists(p));
		if (source == null)
		{
			Debug.LogError("Korean font: no Korean font found. Put one at " + CandidateFonts[0]
						   + " (or another of the paths this script looks in) and run again.");

			if (Application.isBatchMode)
				EditorApplication.Exit(1);

			return;
		}

		Font font = AssetDatabase.LoadAssetAtPath<Font>(source);
		if (font == null)
		{
			Debug.LogError("Korean font: " + source + " did not import as a font");

			if (Application.isBatchMode)
				EditorApplication.Exit(1);

			return;
		}

		TMP_FontAsset asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);

		if (asset == null)
		{
			asset = TMP_FontAsset.CreateFontAsset(font, SamplingPointSize, AtlasPadding,
												  UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
												  AtlasSize, AtlasSize,
												  AtlasPopulationMode.Dynamic);

			if (asset == null)
			{
				Debug.LogError("Korean font: could not build a font asset from " + source);

				if (Application.isBatchMode)
					EditorApplication.Exit(1);

				return;
			}

			AssetDatabase.CreateAsset(asset, FontAssetPath);

			//The atlas and the glyph tables are separate objects that have to live inside the asset, or the
			//font comes back from a fresh session with nothing to draw with
			asset.atlasTextures[0].name = asset.name + " Atlas";
			AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
			AssetDatabase.AddObjectToAsset(asset.material, asset);

			Debug.Log("Korean font: built " + FontAssetPath + " from " + source);
		}
		else
		{
			Debug.Log("Korean font: " + FontAssetPath + " already exists, leaving it as it is");
		}

		int wired = AddAsFallbackTo(asset);
		Debug.Log("Korean font: set as a fallback on " + wired + " font asset(s)");

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}

	/// Hangs the Korean font off every other font asset in the project.
	///
	/// Done per font asset rather than through the project-wide list in TMP Settings, because that list is
	/// only reached for text whose own font has no fallbacks of its own to offer first.
	private static int AddAsFallbackTo(TMP_FontAsset korean)
	{
		int wired = 0;

		foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
		{
			string path = AssetDatabase.GUIDToAssetPath(guid);
			var other = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);

			if (other == null || other == korean)
				continue;

			if (other.fallbackFontAssetTable == null)
				other.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();

			if (other.fallbackFontAssetTable.Contains(korean))
				continue;

			other.fallbackFontAssetTable.Add(korean);
			EditorUtility.SetDirty(other);
			wired++;

			Debug.Log("Korean font: fallback added to " + path);
		}

		return wired;
	}
}
