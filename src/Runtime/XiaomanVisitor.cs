using System;
using System.Collections.Generic;
using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod : IMod
    {
        public const string Id = "sansm.xiaoman.visitor";
        public const string ModName = "深空小满：可能很迷醉？";
        public const string ClientId = "sansm.xiaoman.customer";
        public const string SpriteKey = "sansm.xiaoman.female";
        // Preserve the approved low-resolution geometry; higher-detail frames
        // scale pixel dimensions and pixels-per-unit by the same exact factor.
        private const int FrameWidth = PortraitDetailRules.FrameWidth;
        private const int OriginalHeight = PortraitDetailRules.BodyHeight;
        private const int HeadPadding = 10;
        private const int FrameHeight = OriginalHeight + HeadPadding;

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

        public void Init(ModManifest manifest)
        {
            Instance = this;
            Log = new ModLog(manifest);
            ModConfig config = ModHelper.GetConfig(Id);
            size = config.Define("portrait_scale", 0.84f, "小满 / Xiaoman", () => Text("cfg_size"), min: 0.6f, max: 1.4f);
            Animate = config.Define("animate_expressions", true, "小满 / Xiaoman", () => Text("cfg_animation"));
            shortcut = config.Define("enable_invite_shortcut", true, "小满 / Xiaoman", () => Text("cfg_shortcut"),
                tooltip: () => Text("cfg_shortcut_tip"));
            experimentalTavern = config.Define(TavernRules.ExperimentalConfigKey, TavernRules.ExperimentalDefault,
                "实验性 / Experimental", () => Text("cfg_experimental_tavern"),
                tooltip: () => Text("cfg_experimental_tavern_tip"), requiresRestart: true);
            InitVoice(config);
            size.OnChanged += _ => { registeredDictionary = null; };
            Log.Log("Initialized: independent customer, original portrait layers, three temporary drink effects.");
        }

        public void OnEnable()
        {
            enabled = true;
            CaptureTavernSetting();
            ModHook.OnModAssetsLoaded += OnAssetsLoaded;
            ModHook.OnGameLoadedEarly += OnGameLoaded;
            ModHook.OnGameLoadedLate += OnGameLoaded;
            ModHook.OnGenerateCustomerVeryLate += GenerateVisitor;
            ModHook.OnShutterOpenedLate += QueueOpeningVisitor;
            EnableGifts();
            EnableCocktails();
            EnablePaidWork();
            EnableWeekFour();
            EnsureTavernMap();
            ModHelper.AddDebugFunction(Id, ModHelper.DEBUG_CATEGORY_SPAWN, "Invite Xiaoman", () => Invite());
            driverObject = new GameObject("XiaomanVisitor.Animator");
            UnityEngine.Object.DontDestroyOnLoad(driverObject);
            driverObject.AddComponent<XiaomanAnimator>();
            nativeRendering = driverObject.AddComponent<XiaomanNativeRendering>();
            Voice = driverObject.AddComponent<XiaomanVoicePlayer>();
            Voice.Configure(this);
            ModHelper.AddDebugFunction(Id, ModHelper.DEBUG_CATEGORY_SPAWN, "Preview Xiaoman voice", () => Voice?.Preview());
            foreach (LoadedMod mod in ModLoader.Mods)
                if (mod.Manifest.ID == Id) OnAssetsLoaded(mod);
        }

        public void OnDisable()
        {
            ShutdownGunReaction();
            enabled = false;
            if (nativeRendering != null) nativeRendering.Shutdown();
            XiaomanLetterInk.ReleaseAll();
            if (Voice != null) Voice.Shutdown();
            ModHook.OnModAssetsLoaded -= OnAssetsLoaded;
            ModHook.OnGameLoadedEarly -= OnGameLoaded;
            ModHook.OnGameLoadedLate -= OnGameLoaded;
            ModHook.OnGenerateCustomerVeryLate -= GenerateVisitor;
            ModHook.OnShutterOpenedLate -= QueueOpeningVisitor;
            DisableGifts();
            DisableCocktails();
            DisablePaidWork();
            DisableWeekFour();
            DisableTavernMap();
            ModHelper.RemoveDebugFunctions(Id);
            if (PlayerStore.HasInstance && PlayerStore.Instance.storeClientManager != null)
                PlayerStore.Instance.storeClientManager.clientStack.RemoveAll(c => c != null && CustomerQueuePolicy.IsXiaoman(c.identifier));
            if (registeredDictionary != null) registeredDictionary.Remove(SpriteKey);
            DestroySprites();
            ReleasePortraitAssets();
            ReleaseGunPortraits();
            if (driverObject != null) UnityEngine.Object.Destroy(driverObject);
            if (Instance == this) Instance = null;
        }

        private void OnAssetsLoaded(LoadedMod mod)
        {
            if (!PreparePortraitAssets(mod)) return;
            SetGiftAssetsReady(mod);
            ValidateCocktailAssets(mod);
            LoadVoicePack(mod);
        }

        private void OnGameLoaded()
        {
            CloseTavernCassettePlayer();
            ResetGunReaction();
            Voice?.Stop();
            moodOwner = null;
            EnsurePortraits();
            EnsureCassetteCaseStorage();
            EnsureTavernMap();
            RecoverTavernDrink();
        }

        private void GenerateVisitor(StoreClientManager manager)
        {
            if (StoreStation.Instance == null || StoreStation.Instance.dayCounter < 1) return;
            Queue(manager);
        }

        private void QueueOpeningVisitor()
        {
            if (PlayerStore.HasInstance && PlayerStore.Instance.storeState == PlayerStore.StoreState.OPEN)
                Queue(PlayerStore.Instance.storeClientManager);
        }

        internal bool Invite()
        {
            if (!PlayerStore.HasInstance || StoreClientMono.Instance == null || PlayerStore.Instance.storeState != PlayerStore.StoreState.OPEN)
            {
                Log.Log("Invite Xiaoman: open the shop first.");
                return false;
            }
            return Queue(PlayerStore.Instance.storeClientManager);
        }

        private bool Queue(StoreClientManager manager)
        {
            if (!PlayerStore.HasInstance || manager == null || manager.clientStack == null || StoreStation.Instance == null || !EnsurePortraits()) return false;
            StoreClient current = PlayerStore.Instance.currentClientInstance == null ? null : PlayerStore.Instance.currentClientInstance.GetClientBlueprint();
            if (current != null && CustomerQueuePolicy.IsXiaoman(current.identifier)) return false;
            StoreClient queued = manager.clientStack.Find(c => c != null && CustomerQueuePolicy.IsXiaoman(c.identifier));
            if (queued != null)
            {
                // OnShutterOpenedLate runs after native future clients have also
                // been queued. Repair older front/middle placements using only
                // Xiaoman's entry, including invitations made through the shortcut.
                if (CustomerQueuePolicy.MoveToTail(manager.clientStack, queued))
                    Log.Log("Moved pending Xiaoman behind all existing customers; native order preserved.");
                return false;
            }
            int today = StoreStation.Instance.dayCounter;
            var kind=CustomerQueuePolicy.Next(today,ReadStory(),ReadWork(),TavernMapEnabled);
            if (kind==XiaomanVisitKind.None) return false;
            StoreClient client = kind==XiaomanVisitKind.Story ? CreateVisitor() : CreateWorkThanksVisitor();
            ModHelper.SetClientSource(client, Id);
            manager.AddClient(client);
            if (!manager.clientStack.Contains(client)) return false;
            // The native AddClient appends after generation, with no replacement
            // or reduction of the normal/scripted customer count for this day.
            Log.Log("Queued additional Xiaoman at the tail on day " + today + "; all existing customers retained.");
            return true;
        }

        private StoreClient CreateVisitor()
        {
            return CreateStoryVisitor(ReadStory().VisitIndex + 1);
        }

        private Dialogue Line(StoreClient client, string key, Mood expression, params object[] args)
        {
            return BindVoice(new Dialogue().SetText(client.displayName, Text(key, args))
                .SetStartAction(() => SetMood(client, expression)), key, expression: expression);
        }

        private Dialogue Append(Dialogue previous, StoreClient client, string key, Mood expression, params object[] args)
        {
            Dialogue next = Line(client, key, expression, args);
            previous.nextDialogue = next;
            return next;
        }

        internal bool ShortcutEnabled { get { return shortcut.Value; } }
        private static string Text(string key, params object[] args) { return ModHelper.GetLocalized(Id, key, args); }
    }

}
