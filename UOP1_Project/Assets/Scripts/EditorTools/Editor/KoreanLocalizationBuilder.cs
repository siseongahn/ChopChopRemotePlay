using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Tables;

/// <summary>
/// Adds Korean to the project's locales and fills its string tables from the translations kept alongside
/// the project.
/// </summary>
/// <remarks>
/// Done through the editor API rather than by writing the assets out by hand. A locale is not just its own
/// asset: adding one has to register it with Addressables, and each table it gains needs an address, a
/// label and an entry in its collection. AddNewTable does all of that, and getting any of it wrong by hand
/// shows up as a language that is present in the menu and empty at runtime.
///
/// The translations live in LocalizationKorean beside Assets, so they are readable as plain text and are
/// not dragged into the project as assets of their own. Entries are keyed by table entry id, which is what
/// survives a line being reworded in English.
/// </remarks>
public static class KoreanLocalizationBuilder
{
	private const string LocaleCode = "ko";
	private const string LocaleName = "Korean (ko)";
	private const string LocaleFolder = "Assets/LocalizationFiles/Locales";

	//Tables to fill, and the file each is filled from
	private static readonly (string table, string file)[] Sources =
	{
		("Questline Dialogue", "dialogue_ko.json"),
		("Actors", "actors_ko.json"),
	};

	[MenuItem("Tools/Add Korean Localization")]
	public static void Build()
	{
		int failures = 0;

		try
		{
			Locale korean = EnsureLocale();

			FallBackToEnglish(korean);

			foreach ((string table, string file) in Sources)
				failures += Fill(table, file, korean);

			GiveNewGroupsTheirBuildPaths();

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}
		catch (Exception e)
		{
			Debug.LogError("Korean localization: " + e);
			failures++;
		}

		Debug.Log("Korean localization: finished with " + failures + " failure(s)");

		//Batchmode reports success whatever happened in here, so the exit code is set from what we saw
		if (failures > 0 && Application.isBatchMode)
			EditorApplication.Exit(1);
	}

	private static Locale EnsureLocale()
	{
		Locale existing = LocalizationEditorSettings.GetLocales()
			.FirstOrDefault(l => l != null && l.Identifier.Code == LocaleCode);

		if (existing != null)
		{
			Debug.Log("Korean localization: locale already present");
			return existing;
		}

		Locale korean = Locale.CreateLocale(LocaleCode);
		korean.name = LocaleName;

		if (!Directory.Exists(LocaleFolder))
			Directory.CreateDirectory(LocaleFolder);

		AssetDatabase.CreateAsset(korean, LocaleFolder + "/" + LocaleName + ".asset");

		//This is what puts it in front of the game: the locale provider reads the locales out of
		//Addressables, so an asset that is merely on disk is a language nobody can pick
		LocalizationEditorSettings.AddLocale(korean);

		Debug.Log("Korean localization: added locale " + LocaleName);
		return korean;
	}

	/// <summary>
	/// Points Korean at English for anything Korean has not been given.
	/// </summary>
	/// <remarks>
	/// Only the dialogue is translated, and a locale with no table for a collection does not quietly leave
	/// the English behind - it shows the key instead, so the menus came up reading Quit_Popup_Title and
	/// NewGame_Popup_Description. A fallback locale is what turns a missing translation back into the
	/// English line it was written from, which is what the menus should read until they are translated too.
	/// </remarks>
	private static void FallBackToEnglish(Locale korean)
	{
		if (korean.Metadata.GetMetadata<FallbackLocale>() != null)
			return;

		Locale english = LocalizationEditorSettings.GetLocales()
			.FirstOrDefault(l => l != null && l.Identifier.Code == "en");

		if (english == null)
		{
			Debug.LogWarning("Korean localization: no English locale to fall back to");
			return;
		}

		korean.Metadata.AddMetadata(new FallbackLocale(english));
		EditorUtility.SetDirty(korean);

		Debug.Log("Korean localization: untranslated entries will fall back to English");
	}

	private static int Fill(string tableName, string file, Locale locale)
	{
		StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(tableName);
		if (collection == null)
		{
			Debug.LogError("Korean localization: no string table collection called '" + tableName + "'");
			return 1;
		}

		string path = Path.Combine(Application.dataPath, "..", "LocalizationKorean", file);
		if (!File.Exists(path))
		{
			Debug.LogError("Korean localization: " + path + " is not there");
			return 1;
		}

		Payload payload = JsonUtility.FromJson<Payload>(File.ReadAllText(path, System.Text.Encoding.UTF8));
		if (payload?.items == null || payload.items.Count == 0)
		{
			Debug.LogError("Korean localization: nothing to read out of " + file);
			return 1;
		}

		StringTable table = collection.StringTables
			.FirstOrDefault(t => t != null && t.LocaleIdentifier.Code == LocaleCode);

		if (table == null)
		{
			table = collection.AddNewTable(locale.Identifier) as StringTable;
			Debug.Log("Korean localization: created a Korean table for '" + tableName + "'");
		}

		if (table == null)
		{
			Debug.LogError("Korean localization: could not get a Korean table for '" + tableName + "'");
			return 1;
		}

		//Only ids the shared data still knows about are written. One left over from a line that has since
		//been deleted would otherwise reappear as an entry nothing can ever ask for.
		var known = new HashSet<long>(collection.SharedData.Entries.Select(e => e.Id));

		int written = 0, unknown = 0;
		foreach (Entry entry in payload.items)
		{
			if (!known.Contains(entry.id))
			{
				unknown++;
				continue;
			}

			table.AddEntry(entry.id, entry.ko);
			written++;
		}

		EditorUtility.SetDirty(table);
		EditorUtility.SetDirty(collection);

		Debug.Log("Korean localization: '" + tableName + "' wrote " + written + " entries"
				  + (unknown > 0 ? ", skipped " + unknown + " the table no longer has" : ""));

		return 0;
	}

	/// <summary>
	/// Points any group that came out without them at the profile's local build and load paths.
	/// </summary>
	/// <remarks>
	/// A group made for a new locale here arrives with its build and load paths unset, where the ones made
	/// through the editor window carry the profile variables. Addressables then looks those ids up while
	/// building and finds nothing, and the whole content build fails on the spot with "the given key was not
	/// present in the dictionary" - no group named, nothing built.
	///
	/// Every group in the project uses the same two variables, so unset means wrong rather than deliberate.
	/// </remarks>
	private static void GiveNewGroupsTheirBuildPaths()
	{
		AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
		if (settings == null)
		{
			Debug.LogWarning("Korean localization: no Addressables settings to check group paths against");
			return;
		}

		foreach (AddressableAssetGroup group in settings.groups)
		{
			if (group == null)
				continue;

			BundledAssetGroupSchema schema = group.GetSchema<BundledAssetGroupSchema>();
			if (schema == null)
				continue;

			bool repaired = false;

			if (string.IsNullOrEmpty(schema.BuildPath.Id))
				repaired |= schema.BuildPath.SetVariableByName(settings, "LocalBuildPath");

			if (string.IsNullOrEmpty(schema.LoadPath.Id))
				repaired |= schema.LoadPath.SetVariableByName(settings, "LocalLoadPath");

			if (!repaired)
				continue;

			EditorUtility.SetDirty(schema);
			EditorUtility.SetDirty(group);
			Debug.Log("Korean localization: gave '" + group.Name + "' its build and load paths");
		}
	}

	[Serializable]
	private class Payload
	{
		public List<Entry> items;
	}

	[Serializable]
	private class Entry
	{
		public long id;
		public string ko;
	}
}
