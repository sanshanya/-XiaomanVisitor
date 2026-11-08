using System;
using System.Collections.Generic;
using UnityEngine;

namespace XiaomanVisitor
{
    public enum Mood { Neutral, Soft, Closed, Smile, Shy, Worried }

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
            InitVoice(config);
            size.OnChanged += _ => { registeredDictionary = null; };
            Log.Log("Initialized: independent customer, original portrait layers, three temporary drink effects.");
        }

        public void OnEnable()
        {
            enabled = true;
            ModHook.OnModAssetsLoaded += OnAssetsLoaded;
            ModHook.OnGameLoadedEarly += OnGameLoaded;
            ModHook.OnGameLoadedLate += OnGameLoaded;
            ModHook.OnGenerateCustomerVeryLate += GenerateVisitor;
            ModHook.OnShutterOpenedLate += QueueOpeningVisitor;
            EnableGifts();
            EnableCocktails();
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
            DisableTavernMap();
            ModHelper.RemoveDebugFunctions(Id);
            if (PlayerStore.HasInstance && PlayerStore.Instance.storeClientManager != null)
                PlayerStore.Instance.storeClientManager.clientStack.RemoveAll(c => c != null && c.identifier == ClientId);
            if (registeredDictionary != null) registeredDictionary.Remove(SpriteKey);
            DestroySprites();
            ReleaseHighDetailPortraits();
            if (driverObject != null) UnityEngine.Object.Destroy(driverObject);
            if (Instance == this) Instance = null;
        }

        private void OnAssetsLoaded(LoadedMod mod)
        {
            if (mod.Manifest.ID != Id) return;
            Texture2D loaded;
            if (!mod.Textures.TryGetValue("xiaoman_atlas", out loaded))
            {
                Log.Error("Missing Textures/xiaoman_atlas.png; visitor generation is disabled.");
                return;
            }
            // The general-purpose mod loader creates mipmaps. Point filtering alone
            // still selects their averaged pixels when the world camera shrinks a sprite.
            // Keep the full-resolution pixels and replace only this mod's own texture.
            if (loaded.mipmapCount > 1)
            {
                atlas = new Texture2D(loaded.width, loaded.height, TextureFormat.RGBA32, false);
                atlas.SetPixels32(loaded.GetPixels32(0));
                atlas.Apply(false, true);
                mod.Textures["xiaoman_atlas"] = atlas;
                UnityEngine.Object.Destroy(loaded);
            }
            else atlas = loaded;
            atlas.filterMode = FilterMode.Point;
            atlas.wrapMode = TextureWrapMode.Clamp;
            atlas.anisoLevel = 0;
            atlas.name = "XiaomanAtlas.Point.NoMipmaps";
            Log.Log("Portrait atlas ready: " + atlas.width + "x" + atlas.height
                + ", 18 frames, mipmaps=" + atlas.mipmapCount + ", filter=" + atlas.filterMode + ".");
            EnsurePortraits();
            SetGiftAssetsReady(mod);
            ValidateCocktailAssets(mod);
            LoadVoicePack(mod);
        }

        private void OnGameLoaded()
        {
            Voice?.Stop();
            moodOwner = null;
            EnsurePortraits();
            EnsureCassetteCaseStorage();
            EnsureTavernMap();
        }

        private void DestroySprites()
        {
            for (int row = 0; row < 6; row++)
                for (int col = 0; col < 3; col++)
                {
                    if (frames[row, col] != null) UnityEngine.Object.Destroy(frames[row, col]);
                    frames[row, col] = null;
                }
            activePortraitAtlas = null;
        }

        internal bool EnsurePortraits()
        {
            if (!enabled || atlas == null || SpriteDict.Instance == null || SpriteDict.Instance.spriteDictionary == null) return false;
            var dict = SpriteDict.Instance.spriteDictionary;
            Texture2D selectedAtlas = SelectPortraitAtlas();
            bool highDetail = selectedAtlas != null && selectedAtlas != atlas;
            int detailScale = highDetail ? PortraitDetailRules.HighScale : 1;
            int columns = highDetail ? PortraitDetailRules.HighColumns : 3;
            int rows = highDetail ? PortraitDetailRules.HighRows : 6;
            int frameWidth = FrameWidth * detailScale, frameHeight = FrameHeight * detailScale;
            if (registeredDictionary == dict && activePortraitAtlas == selectedAtlas && frames[0, 0] != null)
            {
                dict[SpriteKey] = frames[0, 0];
                return true;
            }
            if (selectedAtlas.width != frameWidth * columns || selectedAtlas.height != frameHeight * rows)
            {
                Log.Error("Portrait atlas dimensions do not match this mod version.");
                return false;
            }
            DestroySprites();
            activePortraitAtlas = selectedAtlas;
            Sprite reference;
            float referenceHeight = 2.11f;
            float referenceBottom = 1.055f;
            if (dict.TryGetValue("femaleLower1", out reference) && reference != null)
            {
                referenceHeight = reference.bounds.size.y;
                referenceBottom = reference.pivot.y / reference.pixelsPerUnit;
            }
            // Added headroom extends the artwork upward without resizing or moving the original body.
            float ppu = Mathf.Round(OriginalHeight / (referenceHeight * 0.91f * size.Value)) * detailScale;
            var pivot = new Vector2(0.5f, referenceBottom * ppu / frameHeight);
            for (int row = 0; row < 6; row++)
                for (int col = 0; col < 3; col++)
                {
                    int index = row * 3 + col;
                    var rect = new Rect((index % columns) * frameWidth, selectedAtlas.height - (index / columns + 1) * frameHeight, frameWidth, frameHeight);
                    Sprite sprite = Sprite.Create(selectedAtlas, rect, pivot, ppu, 0, SpriteMeshType.FullRect);
                    sprite.name = "Xiaoman_" + (Mood)row + "_" + col;
                    frames[row, col] = sprite;
                }
            dict[SpriteKey] = frames[0, 0];
            registeredDictionary = dict;
            Log.Log("Registered independent portrait; scale=" + size.Value + ", pixelsPerUnit=" + ppu
                + ", detail=" + detailScale + "x, viewportHeight=" + LetterboxManager.GameAreaHeight + ".");
            return true;
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
            if (current != null && current.identifier == ClientId) return false;
            StoreClient queued = manager.clientStack.Find(c => c != null && c.identifier == ClientId);
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
            if (!ReadStory().MayVisit(today)) return false;
            StoreClient client = CreateVisitor();
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

        private void SetMood(StoreClient client, Mood expression) { moodOwner = client; mood = expression; }
        internal Mood GetMood(StoreClient client) { return client == moodOwner ? mood : Mood.Neutral; }
        internal Sprite Frame(Mood expression, int mouth) { return frames[(int)expression, mouth]; }
        internal bool ShortcutEnabled { get { return shortcut.Value; } }
        private static string Text(string key, params object[] args) { return ModHelper.GetLocalized(Id, key, args); }
    }

    public sealed class XiaomanAnimator : MonoBehaviour
    {
        private StoreClient previousClient;
        private float nextBlink;
        private float blinkStart = -100f;

        private void Update()
        {
            XiaomanMod mod = XiaomanMod.Instance;
            if (mod == null) return;
            mod.TickStoryStock();
            if (mod.ShortcutEnabled && !KeyBindHelper.SuppressHotkeys && Input.GetKeyDown(KeyCode.M)
                && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) mod.Invite();
        }

        private void LateUpdate()
        {
            XiaomanMod mod = XiaomanMod.Instance;
            // PlayerStore.Instance creates a store when none exists. Check HasInstance first,
            // so the main menu cannot accidentally initialize inventory rendering.
            if (mod == null || !PlayerStore.HasInstance || StoreClientMono.Instance == null) return;
            StoreClientInstance instance = PlayerStore.Instance.currentClientInstance;
            StoreClient client = instance == null ? null : instance.GetClientBlueprint();
            if (client == null || client.identifier != XiaomanMod.ClientId)
            {
                previousClient = null;
                return;
            }
            SpriteRenderer renderer = StoreClientMono.Instance.image;
            if (renderer == null || !renderer.gameObject.activeInHierarchy || !mod.EnsurePortraits()) return;
            float now = Time.unscaledTime;
            if (previousClient != client)
            {
                previousClient = client;
                nextBlink = now + UnityEngine.Random.Range(2.8f, 5.2f);
                blinkStart = -100f;
            }
            // Idle always uses the original open-eyed, straight-mouth portrait.
            // Dialogue and audio share one line identity. The typewriter is only
            // a fallback when that line has never started a packaged recording.
            Mood expression = Mood.Neutral;
            int mouth = 0;
            if (mod.Animate.Value)
            {
                DialogUIManager dialog = DialogUIManager.Instance;
                Dialogue line = dialog == null ? null : dialog.currentDialog;
                bool visible = line != null && line.title == client.displayName
                    && dialog.clientBubble != null && dialog.clientBubble.gameObject.activeInHierarchy;
                bool typing = visible && dialog.clientBubble.typewriter != null && dialog.clientBubble.typewriter.isShowingText;
                Mood authored;
                bool canSpeak;
                if (!mod.TryLinePerformance(line, out authored, out canSpeak))
                { authored = Mood.Neutral; canSpeak = true; }
                int voiceMouth = 0;
                bool voiceBusy = mod.Voice != null && mod.Voice.PoseFor(line, out voiceMouth);
                bool hadVoice = mod.Voice != null && mod.Voice.HasPlayedFor(line);
                DialoguePose pose = PortraitTiming.Resolve(visible, canSpeak, authored, hadVoice, voiceBusy, typing, voiceMouth, now);
                expression = pose.Expression; mouth = pose.Mouth;
                if (now >= nextBlink)
                {
                    blinkStart = now;
                    nextBlink = now + UnityEngine.Random.Range(3.0f, 5.8f);
                }
                float phase = now - blinkStart;
                if (PortraitTiming.MayBlink(expression) && phase < 0.24f)
                    expression = (phase < 0.06f || phase > 0.18f) ? Mood.Soft : Mood.Closed;
            }
            Sprite frame = mod.Frame(expression, mouth);
            if (frame != null && renderer.sprite != frame) renderer.sprite = frame;
        }
    }
}
