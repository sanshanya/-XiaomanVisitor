using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace XiaomanVisitor
{
    // Alternative entry point. The test assembly compiles the real gun and
    // portrait modules, without story, gifts, trading, tavern or audio modules.
    public sealed partial class XiaomanMod : IMod
    {
        public const string Id = "sansm.xiaoman.gun_test";
        public const string ModName = "小满抬枪测试版（独立）";
        public const string ClientId = "sansm.xiaoman.gun_test.customer";
        public const string SpriteKey = "sansm.xiaoman.gun_test.female";
        private const int FrameWidth = PortraitDetailRules.FrameWidth;
        private const int OriginalHeight = PortraitDetailRules.BodyHeight;
        private const int FrameHeight = PortraitDetailRules.FrameHeight;
        internal static XiaomanMod Instance;
        internal ModLog Log;
        private ConfigEntry<float> size;
        internal ConfigEntry<bool> Animate;
        private ConfigEntry<bool> shortcut;
        private readonly Sprite[,] frames = new Sprite[6, 3];
        private Dictionary<string, Sprite> registeredDictionary;
        private Texture2D atlas;
        private GameObject driverObject;
        private XiaomanNativeRendering nativeRendering;
        private StoreClient moodOwner;
        private Mood mood;
        private bool enabled;
        internal readonly SilentTestVoice Voice = null;
        private sealed class Performance { internal Mood Mood; }
        private readonly ConditionalWeakTable<Dialogue, Performance> lines = new ConditionalWeakTable<Dialogue, Performance>();

        public void Init(ModManifest manifest)
        {
            Instance = this; Log = new ModLog(manifest);
            ModConfig config = ModHelper.GetConfig(Id);
            size = config.Define("portrait_scale", .84f, "抬枪测试 / Gun Test", () => Text("cfg_size"), min: .6f, max: 1.4f);
            Animate = config.Define("animate_expressions", true, "抬枪测试 / Gun Test", () => Text("cfg_animation"));
            shortcut = config.Define("enable_invite_shortcut", true, "抬枪测试 / Gun Test", () => Text("cfg_shortcut"),
                tooltip: () => Text("cfg_shortcut_tip"));
            size.OnChanged += _ => registeredDictionary = null;
        }

        public void OnEnable()
        {
            enabled = true;
            ModHook.OnModAssetsLoaded += AssetsLoaded;
            ModHook.OnGameLoadedLate += GameLoaded;
            ModHook.OnShutterOpenedLate += ShopOpened;
            driverObject = new GameObject("XiaomanGunTest.Animator");
            UnityEngine.Object.DontDestroyOnLoad(driverObject);
            driverObject.AddComponent<XiaomanAnimator>();
            nativeRendering = driverObject.AddComponent<XiaomanNativeRendering>();
            ModHelper.AddDebugFunction(Id, ModHelper.DEBUG_CATEGORY_SPAWN, "Invite gun-test Xiaoman", () => Invite());
            foreach (LoadedMod mod in ModLoader.Mods) if (mod.Manifest.ID == Id) AssetsLoaded(mod);
        }

        private void AssetsLoaded(LoadedMod mod) { PreparePortraitAssets(mod); }
        private void GameLoaded() { ResetGunReaction(); moodOwner = null; EnsurePortraits(); Invite(); }
        private void ShopOpened() { Invite(); }
        internal bool ShortcutEnabled { get { return shortcut.Value; } }
        internal void TickStoryStock() { }

        internal bool Invite()
        {
            if (!enabled || !PlayerStore.HasInstance || StoreStation.Instance == null
                || PlayerStore.Instance.storeState != PlayerStore.StoreState.OPEN || !EnsurePortraits()) return false;
            StoreClientManager manager = PlayerStore.Instance.storeClientManager;
            if (manager == null || manager.clientStack == null || CurrentClientIsXiaoman()
                || manager.clientStack.Exists(c => c != null && c.identifier == ClientId)) return false;
            var client = new StoreClient { identifier = ClientId, displayName = Text("name"), realName = Text("name"),
                spriteName = SpriteKey, clientFaction = StoreClient.FACTION_LOWER,
                clientIntent = StoreClient.ClientIntent.BUY, arrestable = false, noAcceptingContraband = true };
            client.SetBudget(150); client.willAcceptItem = item => false;
            ConfigureGunReaction(client);
            client.mainDialogue = Line(client, "gun_test_intro", Mood.Soft).SetEndAction(() => client.OnIntroduced());
            client.repeatDialogue = Line(client, "gun_test_waiting", Mood.Soft);
            client.OnArrival = () => SetMood(client, Mood.Neutral);
            client.OnDismiss = () => { ResetGunReaction(); SetMood(client, Mood.Neutral); };
            client.CompleteClientCreation();
            ModHelper.SetClientSource(client, Id);
            manager.AddClient(client);
            Log.Log("Gun-test visitor queued at the tail, day=" + StoreStation.Instance.dayCounter + "; no story or gift data.");
            return manager.clientStack.Contains(client);
        }

        private Dialogue Line(StoreClient client, string key, Mood expression)
        {
            Dialogue line = new Dialogue().SetText(client.displayName, Text(key)).SetStartAction(() => SetMood(client, expression));
            lines.Add(line, new Performance { Mood = expression });
            return line;
        }
        internal bool TryLinePerformance(Dialogue line, out Mood expression, out bool canSpeak)
        {
            Performance value; expression = Mood.Neutral; canSpeak = true;
            if (line == null || !lines.TryGetValue(line, out value)) return false;
            expression = value.Mood; return true;
        }
        private static string Text(string key) { return ModHelper.GetLocalized(Id, key); }
        internal static bool CurrentClientIsXiaoman()
        { return PlayerStore.HasInstance && PlayerStore.Instance.currentClientInstance?.GetClientBlueprint().identifier == ClientId; }

        public void OnDisable()
        {
            ShutdownGunReaction(); enabled = false;
            ModHook.OnModAssetsLoaded -= AssetsLoaded;
            ModHook.OnGameLoadedLate -= GameLoaded;
            ModHook.OnShutterOpenedLate -= ShopOpened;
            if (nativeRendering != null) nativeRendering.Shutdown();
            ModHelper.RemoveDebugFunctions(Id);
            if (PlayerStore.HasInstance && PlayerStore.Instance.storeClientManager != null)
                PlayerStore.Instance.storeClientManager.clientStack.RemoveAll(c => c != null && c.identifier == ClientId);
            if (registeredDictionary != null) registeredDictionary.Remove(SpriteKey);
            DestroySprites(); ReleasePortraitAssets(); ReleaseGunPortraits();
            if (driverObject != null) UnityEngine.Object.Destroy(driverObject);
            if (Instance == this) Instance = null;
        }
    }

    // The shared animator also supports recorded dialogue. This harness supplies
    // no audio player, so native character events are the only speech clock.
    internal sealed class SilentTestVoice
    {
        internal void Stop() { }
        internal bool PoseFor(Dialogue line, out int mouth) { mouth = 0; return false; }
        internal bool HasPlayedFor(Dialogue line) { return false; }
    }
}
