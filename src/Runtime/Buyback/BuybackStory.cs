using System;
using HarmonyLib;
using UnityEngine;

namespace XiaomanVisitor.SideStories
{
    // Owned by the existing mod lifecycle; this is not another IMod entry.
    internal sealed partial class BuybackStory
    {
        public const string Id = XiaomanMod.Id;
        internal static BuybackStory Instance;
        private ModLog log;
        private BuybackStage stage;
        private bool enabled;
        private int releaseFrame = -1;
        private BuybackRefusalText refusalText;
        private BuybackMusic nightMusic;
        private bool continuingSleep;
        internal ConfigEntry<bool> ReducedEffects;
        internal bool BlocksInput { get { return stage != null || Time.frameCount <= releaseFrame; } }

        internal void Init(XiaomanMod owner)
        {
            visitor = owner;
            log = owner.Log;
            nightMusic = new BuybackMusic(log);
            ReducedEffects = ModHelper.GetConfig(Id).Define("reduced_effects", false,
                "永久回购 / Buyback", () => "减弱闪屏与震动");
        }
        public void OnEnable()
        {
            Instance = this;
            enabled = true;
            ModHook.OnGameLoadedInit += Close;
            ModHook.OnWakingUpLate += OnMorning;
            ModHook.OnPaperOpened += OnPhotoNoteOpened;
            ModHook.OnGameLoadedLate += TryDeliverPhotoNote;
            ModHook.OnShutterOpenedLate += TryDeliverPhotoNote;
            ModHook.OnGameLoadedLate += RecoverLowDoseSale;
            ModHook.OnShutterOpenedLate += RecoverLowDoseSale;
            log.Log("Permanent Buyback enabled in the main mod; requires actual water sales and the return visit.");
        }
        public void OnDisable()
        {
            enabled = false;
            ModHook.OnGameLoadedInit -= Close;
            ModHook.OnWakingUpLate -= OnMorning;
            ModHook.OnPaperOpened -= OnPhotoNoteOpened;
            ModHook.OnGameLoadedLate -= TryDeliverPhotoNote;
            ModHook.OnShutterOpenedLate -= TryDeliverPhotoNote;
            ModHook.OnGameLoadedLate -= RecoverLowDoseSale;
            ModHook.OnShutterOpenedLate -= RecoverLowDoseSale;
            Close();
            if (Instance == this) Instance = null;
        }

        internal bool InterceptSleep(Action originalSleep)
        {
            if (continuingSleep) return false;
            if (stage != null) return true;
            if (!enabled || !PlayerStore.HasInstance || StoreStation.Instance == null
                || !NightEligible(StoreStation.Instance.dayCounter) || PlayerStore.Instance.playerLocation != 0
                || PlayerStore.Instance.storeState != PlayerStore.StoreState.CLOSED
                || PlayerStore.Instance.currentClientInstance != null
                || (GameOverUIManager.Instance != null && GameOverUIManager.Instance.IsShowing)
                || (MapUIManager.Instance != null && MapUIManager.Instance.isOutside)
                || (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem)
                || StoreUIManager.Instance == null || DialogUIManager.Instance == null) return false;
            GameObject root = null;
            try
            {
                var progress = ReadDoorProgress();
                if (progress.refusals == 3 || (progress.refusals > 0 && progress.day != StoreStation.Instance.dayCounter)) return false;
                int retry = progress.NeedsRetry(StoreStation.Instance.dayCounter) ? progress.refusals : 0;
                root = new GameObject("XiaomanVisitor.BuybackStage", typeof(RectTransform), typeof(Canvas),
                    typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                root.SetActive(false);
                stage = root.AddComponent<BuybackStage>();
                stage.Prepare(log, () => { stage = null; releaseFrame = Time.frameCount + 1; },
                    open => ChooseDoor(open, originalSleep), RefusalText(), retry, nightMusic);
                // No native state has changed until all five images and templates are usable.
                if (EndOfDayUIManager.Instance != null && EndOfDayUIManager.Instance.IsOpen)
                    EndOfDayUIManager.Instance.CloseUI();
                root.SetActive(true);
                stage.Play();
                log.Log("Starting buyback before native settlement; retry=" + retry + ".");
                return true;
            }
            catch (Exception error)
            {
                log.Warning("Buyback story unavailable; native sleep remains available: " + error);
                if (stage != null) stage.Shutdown();
                else if (root != null) UnityEngine.Object.Destroy(root);
                stage = null;
                return false;
            }
        }

        internal void Advance() { stage?.Advance(); }
        private BuybackDoorProgress ReadDoorProgress()
        { return BuybackDoorProgress.Restore(ModHelper.GetModData(Id, BuybackDoorProgress.SaveKey, "")); }
        private void SaveDoorProgress(BuybackDoorProgress progress)
        {
            string raw = progress.Data;
            ModHelper.SetModData(Id, BuybackDoorProgress.SaveKey, raw);
            if (ModHelper.GetModData(Id, BuybackDoorProgress.SaveKey, "") != raw)
                throw new InvalidOperationException("Refusal choice could not be saved");
        }
        private BuybackRefusalText RefusalText()
        {
            if (refusalText == null)
            {
                var text = BuybackStage.ParseData<BuybackRefusalText>(ModHelper.ReadModFileText(Id, "Scenes/Buyback/refusal.json"));
                if (text == null) throw new InvalidOperationException("Missing refusal dialogue/news");
                text.Validate(); refusalText = text;
            }
            return refusalText;
        }
        internal StoreEvent RefusalNews()
        {
            if (!enabled || !PlayerStore.HasInstance || StoreStation.Instance == null) return null;
            try
            {
                var progress = ReadDoorProgress();
                return progress.HasNews(StoreStation.Instance.dayCounter)
                    ? BuybackNewspaper.Article(RefusalText(), StoreStation.Instance.dayCounter) : null;
            }
            catch (Exception error) { log.Warning("Refusal news unavailable: " + error); return null; }
        }
        private void ChooseDoor(bool open, Action originalSleep)
        {
            if (stage == null || !enabled || continuingSleep) return;
            var progress = ReadDoorProgress();
            if (open)
            {
                if (progress.refusals != 0) { progress.Open(); SaveDoorProgress(progress); }
                return;
            }
            progress.Refuse(StoreStation.Instance.dayCounter);
            SaveDoorProgress(progress);
            nightMusic.RetainUntilMorning();
            if (progress.refusals == 3)
            {
                stage.PlayFarewell(() => ResumeSleep(originalSleep));
                return;
            }
            stage.Shutdown(); stage = null;
            log.Log("Door refused; count=" + progress.refusals + "; day=" + progress.day + ".");
        }
        private void ResumeSleep(Action originalSleep)
        {
            stage?.Shutdown(); stage = null;
            continuingSleep = true;
            try { originalSleep(); }
            catch (Exception error) { log.Warning("Native sleep entry failed after refusal: " + error); }
            finally { continuingSleep = false; }
        }
        private void OnMorning()
        {
            if (PlayerStore.HasInstance && StoreStation.Instance != null)
                nightMusic.OnMorning(PlayerStore.Instance.runID, StoreStation.Instance.dayCounter);
            TryDeliverPhotoNote();
        }
        private void Close()
        {
            if (stage != null) stage.Shutdown();
            stage = null;
            nightMusic?.Restore();
            ClosePhotoNote(); endingSubmitted = false;
            refusalText = null;
            ModHelper.StopAllCoroutines(Id);
        }
    }

    [HarmonyPatch(typeof(StoreBedroll), nameof(StoreBedroll.OnClick))]
    public static class BuybackBedPatch
    {
        private static bool Prefix(StoreBedroll __instance)
        { return !(BuybackStory.Instance?.InterceptSleep(__instance.OnClick) ?? false); }
    }

    [HarmonyPatch(typeof(EndOfDayUIManager), nameof(EndOfDayUIManager.OnEndDayButtonClick))]
    public static class BuybackSleepPatch
    {
        private static bool Prefix(EndOfDayUIManager __instance)
        { return !(BuybackStory.Instance?.InterceptSleep(__instance.OnEndDayButtonClick) ?? false); }
    }

    // Native item handlers and store hotkeys poll independently of Unity UI raycasts.
    // Scope both guards to the stage, including the mouse-up frame when it closes.
    [HarmonyPatch(typeof(InputActionManager), "Update")]
    public static class BuybackItemInputPatch
    {
        private static bool Prefix() { return !(BuybackStory.Instance?.BlocksInput ?? false); }
    }

    [HarmonyPatch(typeof(StoreUIManager), "Update")]
    public static class BuybackStoreInputPatch
    {
        private static bool Prefix() { return !(BuybackStory.Instance?.BlocksInput ?? false); }
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.OnAdvancePressed))]
    public static class BuybackAdvancePatch
    {
        private static bool Prefix()
        {
            var mod = BuybackStory.Instance;
            if (mod == null || !mod.BlocksInput) return true;
            mod.Advance();
            return false;
        }
    }
}
