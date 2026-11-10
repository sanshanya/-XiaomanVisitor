using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;
using HarmonyLib;
using Febucci.UI.Core;

namespace XiaomanVisitor
{
    // Clone the game's presentation, never its conversation/transaction state.
    internal sealed class XiaomanTavernBubble
    {
        private readonly RectTransform root, body, name;
        private readonly TextBubble bubble;
        private readonly TextMeshProUGUI nameText;
        private readonly DialogBubbleWidth width;
        private readonly DialogBubbleScaler scaler = new DialogBubbleScaler();
        private readonly Action stopVoice;
        private float completedAt = -1f;
        private float rightEdge, previousScale = -1f;
        private bool layoutDirty;
        internal bool Visible { get { return root.gameObject.activeSelf; } }
        internal bool Typing { get { return Visible && bubble.typewriter.isShowingText; } }

        internal XiaomanTavernBubble(RectTransform parent, Action onAdvance)
        {
            DialogUIManager native = DialogUIManager.Instance;
            if (native == null || native.clientBubble == null || native.clientNameBubble == null)
                throw new InvalidOperationException("Native dialogue templates are not ready");
            stopVoice = onAdvance;
            root = new GameObject("Xiaoman.NativeDialogue", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0, 1);
            root.sizeDelta = Vector2.zero;
            root.gameObject.SetActive(false);
            bubble = UnityEngine.Object.Instantiate(native.clientBubble, root, false);
            body = (RectTransform)bubble.transform;
            name = (RectTransform)UnityEngine.Object.Instantiate(native.clientNameBubble, root, false).transform;
            nameText = name.GetComponentInChildren<TextMeshProUGUI>(true);
            width = body.GetComponent<DialogBubbleWidth>();
            float factor = Mathf.Max(.01f, EmporiumOption.DialogBubbleScale);
            Vector2 gap = (name.anchoredPosition - body.anchoredPosition) / factor;
            foreach (RectTransform rect in new[] { body, name }) {
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
                rect.localScale = Vector3.one;
                // StoreUI.OnDisable closes the original named shop window.
                foreach (StoreUI marker in rect.GetComponentsInChildren<StoreUI>(true)) {
                    marker.identifier = XiaomanMod.Id + ".tavern.dialogue";
                    marker.isPaperUI = false; marker.zoomable = false;
                    marker.enabled = false;
                    UnityEngine.Object.Destroy(marker);
                }
                DialogBubbleClickAdvance.Attach(rect.gameObject, Advance);
                rect.gameObject.SetActive(true);
            }
            body.anchoredPosition = Vector2.zero;
            name.anchoredPosition = gap;
            scaler.Register(body); scaler.Register(name, body);
            // The serialized template calls DialogUIManager.OnCurrentTextDisplayed.
            // Clear all inherited callbacks before activating the local copy.
            Clear(bubble.typewriter.onTextShowed);
            Clear(bubble.typewriter.onTypewriterStart);
            Clear(bubble.typewriter.onTextDisappeared);
            Clear(bubble.typewriter.onCharacterVisible);
            Clear(bubble.typewriter.onMessage);
            bubble.typewriter.onTextShowed.AddListener(() => completedAt = Time.unscaledTime);
            var palette = StoreUIManager.Instance.dialogBubbleList;
            bubble.backgroundImage.sprite = palette.GetSprite("defaultp");
            name.GetComponent<Image>().sprite = palette.GetSprite("default");
        }

        internal static void Clear(UnityEventBase callbacks)
        {
            for (int i = 0; i < callbacks.GetPersistentEventCount(); i++)
                callbacks.SetPersistentListenerState(i, UnityEventCallState.Off);
            callbacks.RemoveAllListeners();
        }

        internal void Show(string speaker, string text)
        {
            Hide();
            DialogUIManager native = DialogUIManager.Instance;
            bubble.textfield.font = native.clientBubble.textfield.font;
            bubble.textfield.fontSize = 18f;
            bubble.textfield.enableAutoSizing=false;
            nameText.font = native.clientNameTMP.font;
            nameText.fontSize = 18f;
            nameText.enableAutoSizing=false;
            nameText.text = speaker;
            layoutDirty = true;
            completedAt = -1f;
            root.gameObject.SetActive(true);
            bubble.typewriter.hideAppearancesOnSkip = false;
            bubble.typewriter.ShowText(text);
            if (!EmporiumOption.Instance.enableTypewriterEffect) {
                bubble.typewriter.hideAppearancesOnSkip = true;
                bubble.typewriter.SkipTypewriter();
            }
        }

        internal void SetSpeed(float speed) { bubble.typewriter.SetTypewriterSpeed(speed); }
        internal void Advance()
        {
            if (!Visible) return;
            stopVoice();
            if (Typing) bubble.typewriter.SkipTypewriter();
            else Hide();
        }

        // Before the native width component's LateUpdate, so it lays out this frame.
        internal void PrepareLayout(RectTransform panel,bool choiceMenu=false)
        {
            if (!Visible) return;
            RectTransform room = (RectTransform)root.parent;
            float unit=room.rect.height/1080f;
            scaler.Apply(unit*EmporiumOption.DialogBubbleScale);
            name.anchoredPosition=new Vector2(0,name.rect.height*name.localScale.y);
            float right = room.rect.width - 16f*unit;
            if (panel != null && panel.gameObject.activeInHierarchy) {
                float edge = room.InverseTransformPoint(panel.TransformPoint(new Vector3(panel.rect.xMin, 0))).x + room.rect.width / 2;
                if (choiceMenu || edge>room.rect.width*.65f) right = Mathf.Min(right, edge - 16f*unit);
            }
            float available = Mathf.Max(120f*unit, right - 16f*unit);
            float maxWidth = Mathf.Min(430f*unit*EmporiumOption.DialogBubbleScale, available);
            rightEdge = right;
            float desired = Mathf.Max(24f, maxWidth / Mathf.Max(.01f, body.localScale.x) - 16f);
            if (width != null && Mathf.Abs(width.maxWidth - desired) > .01f) {
                width.maxWidth = desired; layoutDirty = true;
            }
            float scale = EmporiumOption.DialogBubbleScale;
            if (Mathf.Abs(previousScale - scale) > .001f) { previousScale = scale; layoutDirty = true; }
        }

        internal void Tick(RectTransform actor, bool voiceBusy, bool waitForVoice, Vector2 restPosition,float bottom)
        {
            if (!Visible) return;
            if (completedAt >= 0 && (!waitForVoice || !voiceBusy)
                && Time.unscaledTime - completedAt >= DialogUIManager.ScaleByDialogSpeed(1f) + DialogUIManager.GetTypewriterDelay()) {
                stopVoice(); Hide(); return;
            }
            if (layoutDirty) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(body);
                LayoutRebuilder.ForceRebuildLayoutImmediate(name);
                layoutDirty = false;
            }
            // Dialogue stays at the waist-level anchor while the portrait breathes.
            float x = restPosition.x + actor.rect.width * .55f;
            float y = restPosition.y - actor.rect.height * .9f;
            float actualWidth = body.rect.width * body.localScale.x;
            x = Mathf.Clamp(x, 16f, Mathf.Max(16f, rightEdge - actualWidth));
            y=-Mathf.Max(8,Mathf.Min(-y,bottom-body.rect.height*body.localScale.y-8));
            root.anchoredPosition = new Vector2(x, y);
        }

        internal void Hide()
        {
            bubble.typewriter.StopShowingText();
            root.gameObject.SetActive(false);
            completedAt = -1f;
        }
    }

    [HarmonyPatch(typeof(TypewriterCore), nameof(TypewriterCore.SetTypewriterSpeed))]
    public static class XiaomanTavernTypewriterSpeedPatch
    {
        private static void Postfix(TypewriterCore __instance, float value)
        {
            StoreUIManager ui = StoreUIManager.Instance;
            if (ui != null && __instance == ui.clientTypewriter)
                XiaomanMod.Instance?.SetTavernSpeechSpeed(value);
        }
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.OnAdvancePressed))]
    public static class XiaomanTavernAdvancePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() { return !(XiaomanMod.Instance?.AdvanceTavernSpeech() ?? false); }
    }
}
