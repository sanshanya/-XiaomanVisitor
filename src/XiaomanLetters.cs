using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace XiaomanVisitor
{
    // Fixed letters only. Never touches another mod's papers or global fonts.
    public static class LetterInkRules
    {
        public static string Language(string locale)
        {
            string language = VoiceCatalog.Language(locale);
            return language == "ja" || language == "zh" ? language : "en";
        }

        public static bool TryResolve(string modId, string bookId, string locale, out string path)
        {
            path = null;
            if (modId != XiaomanMod.Id || bookId == null) return false;
            if (bookId == "thank_you_note") bookId = "story_note_01";
            const string prefix = "story_note_";
            int number;
            if (!bookId.StartsWith(prefix, StringComparison.Ordinal)
                || !int.TryParse(bookId.Substring(prefix.Length), NumberStyles.None,
                               CultureInfo.InvariantCulture, out number)
                || number < 1 || number > 14
                || bookId != prefix + number.ToString("00", CultureInfo.InvariantCulture)) return false;
            path = "Letters/" + Language(locale) + "/" + bookId + ".png";
            return true;
        }
    }

    [HarmonyPatch(typeof(PaperUI), "ShowPage")]
    public static class XiaomanLetterPagePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PaperUI __instance)
        {
            if (XiaomanMod.Instance == null || __instance == null || __instance.Data == null
                || __instance.EditorMode || __instance.CurrentPage != 0 || __instance.Content == null) return;
            string unused;
            if (!LetterInkRules.TryResolve(__instance.ModId, __instance.Data.id, "en", out unused)) return;
            Transform page = __instance.Content.Find("page_0");
            if (page == null) return;
            var ink = page.GetComponent<XiaomanLetterInk>();
            if (ink == null) ink = page.gameObject.AddComponent<XiaomanLetterInk>();
            ink.Configure(__instance);
        }
    }

    // Load one localized texture on demand; retain native text as a fallback.
    public sealed class XiaomanLetterInk : MonoBehaviour
    {
        private static readonly HashSet<XiaomanLetterInk> live = new HashSet<XiaomanLetterInk>();
        private static readonly string[] TextIds = { "title", "body", "signature" };
        private PaperUI paper;
        private Texture2D texture;
        private RawImage image;
        private string attemptedLanguage;
        private readonly Dictionary<string, bool> originalVisibility = new Dictionary<string, bool>();
        private readonly Dictionary<string, CustomUIElement> originalElements = new Dictionary<string, CustomUIElement>();

        private void Awake() { live.Add(this); }

        public void Configure(PaperUI owner)
        {
            paper = owner;
            foreach (string id in TextIds)
                if (!originalVisibility.ContainsKey(id) && paper.Has(id))
                {
                    CustomUIElement element = paper.Get(id);
                    if (element != null && element.root != null)
                    {
                        originalVisibility[id] = element.root.activeSelf;
                        originalElements[id] = element;
                    }
                }
            enabled = true;
            RefreshInk();
        }

        private void LateUpdate()
        {
            if (paper == null || XiaomanMod.Instance == null) return;
            string language = LetterInkRules.Language(LocHelper.GetCurrentLocaleCode());
            if (language != attemptedLanguage) RefreshInk();
        }

        private void RefreshInk()
        {
            if (paper == null || !paper.IsVisible || XiaomanMod.Instance == null) return;
            string language = LetterInkRules.Language(LocHelper.GetCurrentLocaleCode());
            if (texture != null && image != null && attemptedLanguage == language)
            {
                image.gameObject.SetActive(true);
                SetOriginalVisible(false);
                return;
            }
            attemptedLanguage = language;
            string path;
            if (!LetterInkRules.TryResolve(paper.ModId, paper.Data.id, language, out path)) return;
            Texture2D pending = null;
            try
            {
                byte[] bytes = ModHelper.ReadModFileBytes(XiaomanMod.Id, path);
                if (bytes == null) throw new InvalidOperationException("Missing letter ink: " + path);
                pending = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(pending, bytes, true)
                    || pending.width != 1122 || pending.height != 1402)
                    throw new InvalidOperationException("Invalid letter ink: " + path);
                pending.filterMode = FilterMode.Bilinear;
                pending.wrapMode = TextureWrapMode.Clamp;
                pending.anisoLevel = 0;
                pending.name = "Xiaoman.LetterInk." + paper.Data.id + "." + language;
                if (image == null)
                {
                    var go = new GameObject("Xiaoman.Yozai.LetterInk", typeof(RectTransform));
                    var rect = (RectTransform)go.transform;
                    rect.SetParent(transform, false);
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                    image = go.AddComponent<RawImage>();
                    image.raycastTarget = false;
                    image.color = Color.white;
                }
                Texture2D previous = texture;
                texture = pending;
                pending = null;
                image.texture = texture;
                image.gameObject.SetActive(true);
                SetOriginalVisible(false);
                if (previous != null) UnityEngine.Object.Destroy(previous);
            }
            catch (Exception error)
            {
                if (pending != null) UnityEngine.Object.Destroy(pending);
                ReleaseInk();
                SetOriginalVisible(true);
                XiaomanMod.Instance?.Log.Warning("Handwritten letter uses native text fallback: " + error.Message);
            }
        }

        private void SetOriginalVisible(bool restore)
        {
            if (paper == null || paper.Root == null) return;
            foreach (var entry in originalVisibility)
            {
                CustomUIElement element;
                // A native Apply/Rebuild destroys the old page at end of frame.
                // Its OnDisable must never restore text in the newly built page.
                if (!originalElements.TryGetValue(entry.Key, out element) || element == null
                    || element.root == null || !element.root.transform.IsChildOf(transform)) continue;
                if (restore && element.text != null)
                    foreach (PaperElementData data in paper.Data.pages[0].elements)
                        if (data.id == entry.Key)
                            element.text.text = paper.ResolveText(data, paper.Data.pages[0]);
                element.root.SetActive(restore && entry.Value);
            }
        }

        private void ReleaseInk()
        {
            if (image != null)
            {
                image.texture = null;
                image.gameObject.SetActive(false);
            }
            if (texture != null) UnityEngine.Object.Destroy(texture);
            texture = null;
        }

        private void OnDisable()
        {
            ReleaseInk();
            attemptedLanguage = null;
            SetOriginalVisible(true);
        }

        private void OnDestroy()
        {
            ReleaseInk();
            live.Remove(this);
        }

        internal static void ReleaseAll()
        {
            foreach (var ink in new List<XiaomanLetterInk>(live))
                if (ink != null)
                {
                    ink.ReleaseInk();
                    ink.SetOriginalVisible(true);
                    ink.enabled = false;
                }
        }
    }
}
