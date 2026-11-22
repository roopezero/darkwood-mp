using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World.Texts
{
    // Tooltips' patches. They run playing alone too: the texts are the
    // game's, not co-op's.

    [HarmonyPatch(
        typeof(global::Language), nameof(global::Language.Get),
        new[] { typeof(string), typeof(string) })]
    static class LanguageGet
    {
        static void Postfix(
            string key, string sheetTitle, ref string __result) =>
            Tooltips.Current.Got(key, sheetTitle, ref __result);
    }

    [HarmonyPatch(
        typeof(global::Language), nameof(global::Language.HasKey),
        new[] { typeof(string), typeof(string) })]
    static class LanguageHasKey
    {
        static void Postfix(
            string key, string sheetTitle, ref bool __result) =>
            Tooltips.Current.HadKey(key, sheetTitle, ref __result);
    }

    /// <summary>The map's popup for a location (Map.Update, every frame the
    /// mouse is on one).</summary>
    [HarmonyPatch(
        typeof(ItemPopup), nameof(ItemPopup.show),
        new[] { typeof(string), typeof(Vector2), typeof(string) })]
    static class ItemPopupShow
    {
        static void Prefix(string _name, ref string descText) =>
            Tooltips.Current.PopupShowing(_name, ref descText);
    }
}
