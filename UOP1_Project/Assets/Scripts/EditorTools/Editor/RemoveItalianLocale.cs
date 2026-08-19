using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

/// <summary>
/// Takes Italian out of the game.
/// </summary>
/// <remarks>
/// Asked for because Italian sat next to Korean in the language list with barely anything translated, so
/// stepping one place left of Korean dropped the game back to English and read as Korean having broken.
///
/// It is not an empty locale - it carries 28 translated entries across the item names, the item
/// descriptions and the menus - and those go with it. Run against a clean tree so it can be reverted.
///
/// Removed through the editor API so the Addressables entries, labels and collection references go too. An
/// asset deleted from under Addressables leaves a registration pointing at nothing.
/// </remarks>
public static class RemoveItalianLocale
{
	private const string LocaleCode = "it";

	[MenuItem("Tools/Remove Italian Locale")]
	public static void Remove()
	{
		Locale italian = LocalizationEditorSettings.GetLocales()
			.FirstOrDefault(l => l != null && l.Identifier.Code == LocaleCode);

		if (italian == null)
		{
			Debug.Log("Italian locale: locale already gone");
			DropEmptyItalianGroup();
			return;
		}

		//Tables first. Dropping the locale while its tables are still in their collections leaves entries
		//that belong to a language the game no longer has.
		int tablesRemoved = 0;

		foreach (StringTableCollection collection in LocalizationEditorSettings.GetStringTableCollections())
		{
			List<StringTable> italianTables = collection.StringTables
				.Where(t => t != null && t.LocaleIdentifier.Code == LocaleCode)
				.ToList();

			foreach (StringTable table in italianTables)
			{
				string path = AssetDatabase.GetAssetPath(table);
				int entries = table.Count;

				collection.RemoveTable(table);
				AssetDatabase.DeleteAsset(path);

				tablesRemoved++;
				Debug.Log("Italian locale: dropped '" + collection.TableCollectionName + "' ("
						  + entries + " entries)");
			}
		}

		string localePath = AssetDatabase.GetAssetPath(italian);
		LocalizationEditorSettings.RemoveLocale(italian);
		AssetDatabase.DeleteAsset(localePath);

		DropEmptyItalianGroup();

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		Debug.Log("Italian locale: removed, along with " + tablesRemoved + " table(s)");

		Report();
	}

	/// Takes away the Addressables group the Italian tables lived in, once nothing is left in it.
	///
	/// Removing the tables empties the group but does not take it away, and an empty group is a row in the
	/// Addressables window that means nothing and a schema pair on disk that nothing reads.
	private static void DropEmptyItalianGroup()
	{
		AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
		if (settings == null)
			return;

		AddressableAssetGroup group = settings.groups
			.FirstOrDefault(g => g != null && g.Name == "Localization-String-Tables-Italian (it)");

		if (group == null)
			return;

		if (group.entries.Count > 0)
		{
			Debug.LogWarning("Italian locale: leaving the group alone, it still holds "
							 + group.entries.Count + " entr(ies)");
			return;
		}

		settings.RemoveGroup(group);
		Debug.Log("Italian locale: removed the empty Addressables group");
	}

	private static void Report()
	{
		string[] left = LocalizationEditorSettings.GetLocales()
			.Where(l => l != null)
			.Select(l => l.LocaleName)
			.ToArray();

		Debug.Log("Italian locale: languages left - " + string.Join(", ", left));
	}
}
