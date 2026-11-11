using System;
using HarmonyLib;
using UnityEngine;

namespace XiaomanVisitor.SideStories
{
    // This entry point is deliberately outside src: production never compiles it.
    public sealed class BuybackTestMod : IMod
    {
        public const string Id = "sansm.xiaoman.buyback_test";
        internal static BuybackTestMod Instance;
        private ModLog log;
        private BuybackStage stage;
        private bool enabled;
        private int releaseFrame = -1;
        internal bool BlocksInput { get { return stage != null || Time.frameCount <= releaseFrame; } }

        public void Init(ModManifest manifest) { log = new ModLog(manifest); }
        public void OnEnable()
        {
            Instance = this;
            enabled = true;
            ModHook.OnGameLoadedInit += Close;
            log.Log("Permanent Buyback: independent day-one sleep rehearsal; no trade/save/main-story writes.");
        }
        public void OnDisable()
        {
            enabled = false;
            ModHook.OnGameLoadedInit -= Close;
            Close();
            if (Instance == this) Instance = null;
        }

        internal bool InterceptSleep()
        {
            if (stage != null) return true;
            if (!enabled || !PlayerStore.HasInstance || StoreStation.Instance == null
                || StoreStation.Instance.dayCounter != 1 || PlayerStore.Instance.playerLocation != 0
                || PlayerStore.Instance.storeState != PlayerStore.StoreState.CLOSED
                || PlayerStore.Instance.currentClientInstance != null
                || (GameOverUIManager.Instance != null && GameOverUIManager.Instance.IsShowing)
                || (MapUIManager.Instance != null && MapUIManager.Instance.isOutside)
                || (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem)
                || StoreUIManager.Instance == null || DialogUIManager.Instance == null) return false;
            GameObject root = null;
            try
            {
                root = new GameObject("XiaomanBuybackTest.Stage", typeof(RectTransform), typeof(Canvas),
                    typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                root.SetActive(false);
                stage = root.AddComponent<BuybackStage>();
                stage.Prepare(log, () => { stage = null; releaseFrame = Time.frameCount + 1; });
                // No native state has changed until all five images and templates are usable.
                if (EndOfDayUIManager.Instance != null && EndOfDayUIManager.Instance.IsOpen)
                    EndOfDayUIManager.Instance.CloseUI();
                root.SetActive(true);
                stage.Play();
                log.Log("Starting five-act rehearsal before native night settlement, day=1.");
                return true;
            }
            catch (Exception error)
            {
                log.Warning("Buyback rehearsal unavailable; native sleep remains available: " + error);
                if (stage != null) stage.Shutdown();
                else if (root != null) UnityEngine.Object.Destroy(root);
                stage = null;
                return false;
            }
        }

        internal void Advance() { stage?.Advance(); }
        private void Close()
        {
            if (stage != null) stage.Shutdown();
            stage = null;
            ModHelper.StopAllCoroutines(Id);
        }
    }

    [HarmonyPatch(typeof(StoreBedroll), nameof(StoreBedroll.OnClick))]
    public static class BuybackBedPatch
    {
        private static bool Prefix() { return !(BuybackTestMod.Instance?.InterceptSleep() ?? false); }
    }

    [HarmonyPatch(typeof(EndOfDayUIManager), nameof(EndOfDayUIManager.OnEndDayButtonClick))]
    public static class BuybackSleepPatch
    {
        private static bool Prefix() { return !(BuybackTestMod.Instance?.InterceptSleep() ?? false); }
    }

    // Native item handlers and store hotkeys poll independently of Unity UI raycasts.
    // Scope both guards to the stage, including the mouse-up frame when it closes.
    [HarmonyPatch(typeof(InputActionManager), "Update")]
    public static class BuybackItemInputPatch
    {
        private static bool Prefix() { return !(BuybackTestMod.Instance?.BlocksInput ?? false); }
    }

    [HarmonyPatch(typeof(StoreUIManager), "Update")]
    public static class BuybackStoreInputPatch
    {
        private static bool Prefix() { return !(BuybackTestMod.Instance?.BlocksInput ?? false); }
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.OnAdvancePressed))]
    public static class BuybackAdvancePatch
    {
        private static bool Prefix()
        {
            var mod = BuybackTestMod.Instance;
            if (mod == null || !mod.BlocksInput) return true;
            mod.Advance();
            return false;
        }
    }
}
