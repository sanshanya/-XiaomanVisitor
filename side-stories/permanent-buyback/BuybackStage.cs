using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Newtonsoft.Json;

namespace XiaomanVisitor.SideStories
{
    [Serializable] internal sealed class BuybackScript { public int schema; public string language, dialogue_mode; public BuybackCue[] cues; }
    [Serializable] internal sealed class BuybackCue
    { public string kind, text, key, transition; public string[] options; public float[] gains, retry_gains; public int value, act; public float seconds, hold, x, y, opacity; }
    [Serializable] internal sealed class BuybackAssets { public int schema; public BuybackImage[] images; public BuybackSound[] sounds; public BuybackMusicSpec music; }
    [Serializable] internal sealed class BuybackMusicSpec { public string file, identifier, title, sha256; public bool loop; }
    [Serializable] internal sealed class BuybackImage { public string file; public int width, height; public string sha256; }
    [Serializable] internal sealed class BuybackSound { public string role, identifier, source; public float gain, pan; }

    internal sealed partial class BuybackStage : MonoBehaviour
    {
        private readonly Texture2D[] textures = new Texture2D[5];
        private readonly BuybackLensGraphic[] bloodLayers = new BuybackLensGraphic[3];
        private readonly Dictionary<AudioSource, bool> nativeMutes = new Dictionary<AudioSource, bool>();
        private readonly Dictionary<string, BuybackSound> soundSpecs = new Dictionary<string, BuybackSound>();
        private readonly Dictionary<string, AudioClip> soundClips = new Dictionary<string, AudioClip>();
        private BuybackScript script;
        private ModLog log;
        private Action onClosed;
        private RectTransform speechRoot, body, nameRect, viewRoot, drips, lensRect;
        private CanvasGroup lens;
        private TextBubble bubble;
        private TextMeshProUGUI speaker, transaction, title, hint;
        private GameObject endingControls;
        private RawImage cg, previousCg;
        private Image background, curtain;
        private BuybackDoorShadeGraphic doorShade;
        private AudioSource ambience, effects;
        private BuybackMusic music;
        private BuybackMusicSpec introMusic;
        private bool closing, lineActive, advanceRequested, atTitle, leaving;
        private bool chooseAgain;
        private int shownCharacters;
        private float imageStarted, previousImageStarted, lineStarted, bloodStarted = -1;
        private float ambientLevel = .7f;
        private int startedFrame, lastAdvanceFrame = -1;

        internal void Prepare(ModLog ownerLog, Action closed, Action<bool> doorChoice, BuybackRefusalText branch, int retry, BuybackMusic musicOwner)
        {
            log = ownerLog; onClosed = closed; onDoorChosen = doorChoice; refusalText = branch; retryDoor = retry;
            music = musicOwner;
            script = ParseData<BuybackScript>(Read("script.json"));
            var assets = ParseData<BuybackAssets>(Read("assets.json"));
            Validate(script, assets);
            log.Log("Parsed buyback data: language=" + script.language + "; cues=" + script.cues.Length
                + "; CGs=" + assets.images.Length + "; sound roles=" + assets.sounds.Length + ".");
            for (int i = 0; i < textures.Length; i++)
            {
                byte[] bytes = ModHelper.ReadModFileBytes(BuybackTestMod.Id, "Scenes/" + assets.images[i].file);
                if (bytes == null) throw new InvalidOperationException("Missing CG " + (i + 1));
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    if (BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() != assets.images[i].sha256)
                        throw new InvalidOperationException("CG hash mismatch: " + (i + 1));
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                textures[i] = texture;
                if (!ImageConversion.LoadImage(texture, bytes, true)
                    || texture.width != assets.images[i].width || texture.height != assets.images[i].height)
                    throw new InvalidOperationException("Invalid CG dimensions: " + (i + 1));
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.anisoLevel = 0;
            }
            BuildPresentation();
            LoadSounds(assets.sounds);
            introMusic = assets.music;
        }

        private static string Read(string file)
        {
            string text = ModHelper.ReadModFileText(BuybackTestMod.Id, "Scenes/" + file);
            if (string.IsNullOrEmpty(text)) throw new InvalidOperationException("Missing " + file);
            return text;
        }

        // Runtime and offline validation must execute this exact parser, not two serializers.
        internal static T ParseData<T>(string text) where T : class
        {
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Empty " + typeof(T).Name);
            try
            {
                return JsonConvert.DeserializeObject<T>(text.TrimStart('\uFEFF'),
                    new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None });
            }
            catch (JsonException error)
            {
                throw new InvalidOperationException("Invalid JSON in " + typeof(T).Name, error);
            }
        }

        internal static void Validate(BuybackScript story, BuybackAssets assets)
        {
            if (story == null) throw new InvalidOperationException("Missing buyback script object");
            if (story.schema != 1 || story.language != "zh" || story.cues == null
                || story.cues.Length < 100 || story.cues.Length > 250)
                throw new InvalidOperationException("Invalid buyback script: schema=" + story.schema
                    + "; language=" + (story.language ?? "null") + "; cues=" + (story.cues == null ? -1 : story.cues.Length));
            if (assets == null) throw new InvalidOperationException("Missing buyback asset object");
            if (assets.schema != 1 || assets.images == null || assets.images.Length != 5)
                throw new InvalidOperationException("Invalid buyback assets: schema=" + assets.schema
                    + "; images=" + (assets.images == null ? -1 : assets.images.Length));
            if (story.cues[0].kind != "shop" || story.cues[1].kind != "knock")
                throw new InvalidOperationException("Opening must retain the shop and play door knocks");
            if (story.dialogue_mode != "manual")
                throw new InvalidOperationException("Rehearsal dialogue must wait for player input");
            if (assets.music == null || assets.music.file != "rain_intro.mp3"
                || assets.music.identifier != "rain_intro" || !assets.music.loop
                || assets.music.sha256 == null || assets.music.sha256.Length != 64)
                throw new InvalidOperationException("Missing approved Intro music");
            for (int i = 0; i < 5; i++)
            {
                var image = assets.images[i];
                if (image == null || image.file != "cg_0" + (i + 1) + ".png"
                    || image.width < 512 || image.width > 4096 || image.height < 512 || image.height > 4096
                    || image.sha256 == null || image.sha256.Length != 64)
                    throw new InvalidOperationException("Invalid CG manifest");
            }
            int nextImage = 1, previousAct = 1;
            bool impact = false, black = false, title = false, doorChoice = false, beautyChoice = false, attackLine = false;
            string lastLine = null;
            foreach (var cue in story.cues)
            {
                if (cue == null || title || cue.seconds < 0 || cue.seconds > 10 || float.IsNaN(cue.seconds)
                    || cue.hold < 0 || cue.hold > 3 || float.IsNaN(cue.hold))
                    throw new InvalidOperationException("Invalid cue timing/order");
                switch (cue.kind)
                {
                    case "line":
                        if (string.IsNullOrEmpty(cue.text) || cue.text.Length > 200
                            || cue.act < previousAct || cue.act > previousAct + 1 || cue.act > 5)
                            throw new InvalidOperationException("Invalid spoken line/act order");
                        previousAct = cue.act;
                        lastLine = cue.text;
                        if (cue.value == 3)
                        {
                            if (!impact || nextImage != 3 || cue.transition != "late_reveal" || cue.hold < .5f || cue.hold >= 1)
                                throw new InvalidOperationException("Attack shout must reveal CG03 near its end");
                            attackLine = true;
                        }
                        break;
                    case "image":
                        if (nextImage == 1 && !doorChoice) throw new InvalidOperationException("CG01 bypasses the door decision");
                        if (nextImage > 5) throw new InvalidOperationException("Too many CGs");
                        string expected = new[] { "door_wipe", "dissolve", "late_reveal", "collapse", "dissolve" }[nextImage - 1];
                        if (nextImage == 3 && !attackLine) throw new InvalidOperationException("CG03 precedes its delayed shout");
                        if (cue.transition != expected) throw new InvalidOperationException("Invalid shot transition");
                        if (cue.value != nextImage++) throw new InvalidOperationException("Invalid CG order");
                        break;
                    case "impact":
                        if (nextImage != 3 || impact || cue.value != 3 || cue.seconds <= 0
                            || lastLine != "前辈也没有机会说了。") throw new InvalidOperationException("Three black-screen impacts must precede CG03");
                        impact = true; break;
                    case "black":
                        if (nextImage != 6 || black) throw new InvalidOperationException("Final fade before CG05");
                        black = true; break;
                    case "title": title = true; break;
                    case "shop":
                        if (cue.x < 0 || cue.x > 1 || cue.y < 0 || cue.y > 1 || float.IsNaN(cue.x)
                            || float.IsNaN(cue.y) || cue.opacity < 0 || cue.opacity > .25f || float.IsNaN(cue.opacity))
                            throw new InvalidOperationException("Invalid door scene anchor/shade");
                        break;
                    case "door_choice":
                        if (doorChoice || nextImage != 1 || previousAct != 1)
                            throw new InvalidOperationException("Door decision must precede CG01 exactly once");
                        doorChoice = true; break;
                    case "reply_choice":
                        if (beautyChoice || nextImage != 2 || lastLine != "呐，前辈，我今天美吗？"
                            || cue.key != "beauty" || cue.options == null || string.Join("|", cue.options) != "……|……")
                            throw new InvalidOperationException("Beauty question requires two silent replies");
                        beautyChoice = true; break;
                    case "knock":
                        if (cue.value != 3 || nextImage != 1 || cue.gains == null || cue.gains.Length != 3
                            || !(cue.gains[0] > 0 && cue.gains[0] < cue.gains[1] && cue.gains[1] < cue.gains[2] && cue.gains[2] <= 1)
                            || cue.retry_gains == null || cue.retry_gains.Length != 2
                            || !(cue.gains[2] < cue.retry_gains[0] && cue.retry_gains[0] < cue.retry_gains[1] && cue.retry_gains[1] <= 1))
                            throw new InvalidOperationException("Opening requires three player-triggered knock sequences");
                        break;
                    case "wait": case "fade": case "lock": case "tape": break;
                    default: throw new InvalidOperationException("Unknown rehearsal cue");
                }
            }
            if (nextImage != 6 || previousAct != 5 || !impact || !black || !title || !beautyChoice || !attackLine)
                throw new InvalidOperationException("Incomplete ending");
            if (assets.sounds == null || assets.sounds.Length != 8)
                throw new InvalidOperationException("Missing directed sound roles");
            var roles = new HashSet<string>();
            foreach (var sound in assets.sounds)
                if (sound == null || string.IsNullOrEmpty(sound.role) || string.IsNullOrEmpty(sound.identifier)
                    || !roles.Add(sound.role) || sound.gain < 0 || sound.gain > 1 || float.IsNaN(sound.gain)
                    || sound.pan < -1 || sound.pan > 1 || float.IsNaN(sound.pan)
                    || (sound.source != null && sound.source != "mod"))
                    throw new InvalidOperationException("Invalid directed sound");
            if (!roles.SetEquals(new[] { "knock", "blade", "lunge", "hit", "blood", "fall", "touch", "tape" }))
                throw new InvalidOperationException("Unknown directed sound role");
        }

        private void BuildPresentation()
        {
            var native = DialogUIManager.Instance;
            if (native == null || native.clientBubble == null || native.clientNameBubble == null)
                throw new InvalidOperationException("Native dialogue templates are not ready");
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1;
            background = FullImage("FadeFromShop", Color.clear);
            background.raycastTarget = true;
            ClickAdvance(background.gameObject);
            viewRoot = FullRect("CameraView");
            previousCg = Child("PreviousCG", viewRoot).gameObject.AddComponent<RawImage>();
            previousCg.raycastTarget = false; previousCg.enabled = false;
            cg = Child("CG", viewRoot).gameObject.AddComponent<RawImage>();
            cg.raycastTarget = false; cg.enabled = false;
            doorShade = FullRect("DoorOpeningShadow", cg.transform).gameObject.AddComponent<BuybackDoorShadeGraphic>();
            doorShade.raycastTarget = false; doorShade.gameObject.SetActive(false);
            curtain = FullImage("Transition", Color.clear);
            var lensRoot = FullRect("ScreenSpaceBlood");
            lensRect = lensRoot;
            lens = lensRoot.gameObject.AddComponent<CanvasGroup>(); lens.alpha = 0;
            for (int style = 2; style >= 0; style--)
            {
                var rect = FullRect("LensLayer" + style, lensRoot);
                var graphic = rect.gameObject.AddComponent<BuybackLensGraphic>();
                bloodLayers[style] = graphic;
                graphic.Style = style; graphic.raycastTarget = false;
                if (style == 1) drips = rect;
            }
            speechRoot = Child("NativeSubtitles");
            speechRoot.anchorMin = speechRoot.anchorMax = new Vector2(.5f, 0);
            speechRoot.sizeDelta = Vector2.zero;
            speechRoot.gameObject.SetActive(false);
            bubble = UnityEngine.Object.Instantiate(native.clientBubble, speechRoot, false);
            body = (RectTransform)bubble.transform;
            nameRect = (RectTransform)UnityEngine.Object.Instantiate(native.clientNameBubble, speechRoot, false).transform;
            speaker = nameRect.GetComponentInChildren<TextMeshProUGUI>(true);
            foreach (RectTransform part in new[] { body, nameRect })
            {
                part.anchorMin = part.anchorMax = part.pivot = new Vector2(0, 1);
                part.localScale = Vector3.one;
                foreach (var marker in part.GetComponentsInChildren<StoreUI>(true))
                {
                    marker.identifier = BuybackTestMod.Id + ".subtitles";
                    marker.isPaperUI = false; marker.zoomable = false;
                    marker.enabled = false; UnityEngine.Object.Destroy(marker);
                }
                var inheritedClick = part.GetComponent<DialogBubbleClickAdvance>();
                if (inheritedClick != null) { inheritedClick.onClick = null; UnityEngine.Object.Destroy(inheritedClick); }
                ClickAdvance(part.gameObject);
                part.gameObject.SetActive(true);
            }
            body.anchoredPosition = Vector2.zero;
            Clear(bubble.typewriter.onTextShowed); Clear(bubble.typewriter.onTypewriterStart);
            Clear(bubble.typewriter.onTextDisappeared); Clear(bubble.typewriter.onCharacterVisible);
            Clear(bubble.typewriter.onMessage);
            bubble.typewriter.onCharacterVisible.AddListener(c => shownCharacters++);
            bubble.textfield.fontSize = 28; bubble.textfield.enableAutoSizing = false;
            speaker.text = "小满"; speaker.fontSize = 26; speaker.enableAutoSizing = false;
            var palette = StoreUIManager.Instance.dialogBubbleList;
            bubble.backgroundImage.sprite = palette.GetSprite("defaultp");
            nameRect.GetComponent<Image>().sprite = palette.GetSprite("default");
            transaction = Label("Transaction", new Vector2(0, 0), 38);
            transaction.text = "交易完成"; transaction.gameObject.SetActive(false);
            title = Label("EndingTitle", new Vector2(0, 35), 42); title.gameObject.SetActive(false);
            hint = Label("Controls", new Vector2(0, -505), 18);
            hint.text = "点击 / 继续对话键　·　Esc 退出测试";
            hint.color = new Color(.65f, .66f, .72f, 1);
            BuildChoiceMenu();
            endingControls = Child("EndingControls").gameObject;
            endingControls.SetActive(false);
            AddButton("重新选择", new Vector2(-150, -80), Rechoose);
            AddButton("结局", new Vector2(150, -80), () => BuybackTestMod.Instance?.CompleteEnding());
            ambience = gameObject.AddComponent<AudioSource>();
            ambience.playOnAwake = false; ambience.loop = true; ambience.spatialBlend = 0;
            effects = gameObject.AddComponent<AudioSource>();
            effects.playOnAwake = false; effects.spatialBlend = 0; effects.volume = .75f;
            if (StoreSoundPlayer.Instance != null)
            {
                ambience.clip = StoreSoundPlayer.Instance.storeAmbient;
                ambience.outputAudioMixerGroup = StoreSoundPlayer.Instance.ambianceAudioSource?.outputAudioMixerGroup;
                effects.outputAudioMixerGroup = StoreSoundPlayer.Instance.soundAudioSource?.outputAudioMixerGroup;
            }
        }

        private RectTransform Child(string childName, Transform parent = null)
        {
            var part = new GameObject(childName, typeof(RectTransform)).GetComponent<RectTransform>();
            part.SetParent(parent == null ? transform : parent, false);
            return part;
        }
        private Image FullImage(string imageName, Color color)
        {
            var rect = FullRect(imageName);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false;
            return image;
        }
        private RectTransform FullRect(string childName, Transform parent = null)
        {
            var rect = Child(childName, parent);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }
        private TextMeshProUGUI Label(string labelName, Vector2 position, float size, Transform parent = null)
        {
            // Speech text owns Febucci visibility state. Static labels instead
            // clone the native choice label, which has no typewriter lifecycle.
            var text = UnityEngine.Object.Instantiate(DialogUIManager.Instance.dialogChoiceBubbles[0].TMP, parent == null ? transform : parent, false);
            text.gameObject.name = labelName;
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.localScale = Vector3.one;
            rect.sizeDelta = new Vector2(1250, 90); rect.anchoredPosition = position;
            text.enabled = true; text.gameObject.SetActive(true); text.maskable = false;
            text.canvasRenderer.SetAlpha(1); text.canvasRenderer.DisableRectClipping(); text.canvasRenderer.cull = false;
            text.margin = Vector4.zero;
            text.firstVisibleCharacter = 0;
            foreach (var group in text.GetComponents<CanvasGroup>()) group.alpha = 1;
            text.maxVisibleCharacters = text.maxVisibleWords = text.maxVisibleLines = int.MaxValue;
            text.enableAutoSizing = false; text.overflowMode = TextOverflowModes.Overflow; text.text = "";
            text.fontSize = size; text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false; text.color = new Color(.88f, .86f, .78f, 1);
            return text;
        }
        private void AddButton(string label, Vector2 position, Action click)
        {
            var option = UnityEngine.Object.Instantiate(DialogUIManager.Instance.dialogChoiceBubbles[0], endingControls.transform, false);
            option.gameObject.name = label; option.SetInteractable(true); option.enabled = false;
            var rect = (RectTransform)option.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.localScale = Vector3.one; rect.sizeDelta = new Vector2(270, 65); rect.anchoredPosition = position;
            foreach (var marker in option.GetComponentsInChildren<StoreUI>(true)) { marker.enabled = false; UnityEngine.Object.Destroy(marker); }
            foreach (var fit in option.GetComponents<ContentSizeFitter>()) fit.enabled = false;
            foreach (var width in option.GetComponents<DialogBubbleWidth>()) width.enabled = false;
            foreach (var layout in option.GetComponents<LayoutGroup>()) layout.enabled = false;
            foreach (var button in option.GetComponentsInChildren<Button>(true)) {
                Clear(button.onClick); button.navigation = new Navigation { mode = Navigation.Mode.None };
            }
            var select = option.GetComponent<Button>() ?? option.GetComponentInChildren<Button>(true);
            if (select == null) throw new InvalidOperationException("Native ending choice has no button");
            select.onClick.AddListener(() => { if (atTitle) click(); });
            var text = option.TMP;
            text.enabled = true; text.gameObject.SetActive(true); text.text = label; text.fontSize = 26;
            text.enableAutoSizing = false; text.alignment = TextAlignmentOptions.Center; text.margin = Vector4.zero;
            text.maxVisibleCharacters = text.maxVisibleWords = text.maxVisibleLines = int.MaxValue;
            text.color = new Color(.88f, .86f, .78f, 1); text.raycastTarget = false;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(12, 5); text.rectTransform.offsetMax = new Vector2(-12, -5);
            option.gameObject.SetActive(true);
        }
        private void ClickAdvance(GameObject target)
        {
            var button = target.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(Advance);
        }
        private static void Clear(UnityEventBase callbacks)
        {
            for (int i = 0; i < callbacks.GetPersistentEventCount(); i++)
                callbacks.SetPersistentListenerState(i, UnityEventCallState.Off);
            callbacks.RemoveAllListeners();
        }

        internal void Play()
        {
            if (closing || leaving) return;
            ModHelper.StopAllCoroutines(BuybackTestMod.Id);
            HideLine(); atTitle = false; advanceRequested = false;
            endingControls.SetActive(false); title.gameObject.SetActive(false); transaction.gameObject.SetActive(false);
            cg.enabled = previousCg.enabled = false; curtain.color = background.color = Color.clear;
            lens.alpha = 0; bloodStarted = -1; drips.anchoredPosition = lensRect.anchoredPosition = Vector2.zero;
            SetBloodBeat(0);
            doorScene = true; choiceDecision = -1; choiceMenu.gameObject.SetActive(false);
            doorShade.gameObject.SetActive(false);
            waitingForKnock = knockRequested = false;
            ResetView(); ambientLevel = .7f;
            hint.gameObject.SetActive(true); startedFrame = Time.frameCount; lastAdvanceFrame = -1;
            InputActionManager.current?.ResetExcept(null);
            MuteNative();
            ambience.volume = Mathf.Clamp01(EmporiumOption.Instance.backgroundVolume) * ambientLevel;
            if (ambience.clip != null) ambience.Play();
            // The wrapper catches failures while iterating, rather than abandoning an invisible lock.
            if (ModHelper.StartCoroutine(BuybackTestMod.Id, Guarded(Run())) == null)
                throw new InvalidOperationException("Mod-owned playback coroutine was not started");
        }
        private IEnumerator Guarded(IEnumerator routine)
        {
            while (!closing)
            {
                object current;
                try { if (!routine.MoveNext()) yield break; current = routine.Current; }
                catch (Exception error)
                {
                    log.Warning("Buyback rehearsal stopped; shop restored: " + error);
                    Release(); UnityEngine.Object.Destroy(gameObject); yield break;
                }
                yield return current;
            }
        }
        private IEnumerator Run()
        {
            foreach (var frame in music.Start(introMusic, PlayerStore.Instance.runID, StoreStation.Instance.dayCounter)) yield return frame;
            int firstCue = 0;
            if (chooseAgain)
            {
                foreach (var frame in ShowShop(script.cues[0])) yield return frame;
                int decision = Array.FindIndex(script.cues, c => c.kind == "door_choice");
                ShowLine(script.cues[decision - 1].text);
                bubble.typewriter.SkipTypewriter(); lineActive = false;
                ShowChoices(new[] { "开门", "不开门" }, true);
                while (choiceDecision < 0) yield return null;
                if (choiceDecision != 0) yield break;
                firstCue = Array.FindIndex(script.cues, c => c.kind == "image" && c.value == 1);
            }
            else if (retryDoor > 0)
            {
                foreach (var frame in ShowShop(script.cues[0])) yield return frame;
                foreach (var frame in DoorKnocks(1, true)) yield return frame;
                var lines = retryDoor == 1 ? new[] { refusalText.retry_line } : refusalText.third_lines;
                foreach (var line in lines)
                    foreach (var frame in SpeakLine(line)) yield return frame;
                ShowChoices(new[] { "开门", "不开门" }, true);
                while (choiceDecision < 0) yield return null;
                if (choiceDecision != 0) yield break;
                firstCue = Array.FindIndex(script.cues, c => c.kind == "image" && c.value == 1);
            }
            for (int cueIndex = firstCue; cueIndex < script.cues.Length; cueIndex++)
            {
                var cue = script.cues[cueIndex];
                switch (cue.kind)
                {
                    case "shop":
                        foreach (var frame in ShowShop(cue)) yield return frame;
                        break;
                    case "fade":
                        foreach (var frame in Fade(background, 0, 1, cue.seconds)) yield return frame;
                        break;
                    case "knock":
                        foreach (var frame in DoorKnocks(cue.value)) yield return frame;
                        break;
                    case "door_choice":
                        ShowChoices(new[] { "开门", "不开门" }, true);
                        while (choiceDecision < 0) yield return null;
                        if (choiceDecision != 0) yield break;
                        break;
                    case "reply_choice":
                        ShowChoices(cue.options, false);
                        while (choiceDecision < 0) yield return null;
                        break;
                    case "image":
                        foreach (var frame in ChangeImage(cue)) yield return frame;
                        break;
                    case "line":
                        if (cue.value == 3) { foreach (var frame in SpeakAttack(cue)) yield return frame; }
                        else { foreach (var frame in SpeakLine(cue.text)) yield return frame; }
                        break;
                    case "wait":
                        foreach (var frame in Wait(cue.seconds)) yield return frame;
                        break;
                    case "impact":
                        foreach (var frame in Impact(cue)) yield return frame;
                        break;
                    case "black":
                        HideLine();
                        float endingStart = Time.unscaledTime;
                        while (Time.unscaledTime - endingStart < cue.seconds)
                        {
                            float t = Mathf.Clamp01((Time.unscaledTime - endingStart) / cue.seconds);
                            curtain.color = new Color(0, 0, 0, t);
                            lens.alpha = 1 - t; ambientLevel = Mathf.Lerp(.45f, .20f, t);
                            yield return null;
                        }
                        curtain.color = Color.black; lens.alpha = 0;
                        cg.enabled = false;
                        break;
                    case "lock":
                        Sound(EmporiumAudioPlayer.Instance?.airlock_ext_close, .45f);
                        foreach (var frame in Wait(cue.seconds)) yield return frame;
                        break;
                    case "tape":
                        Sound("tape");
                        foreach (var frame in Wait(cue.seconds)) yield return frame;
                        break;
                    case "title":
                        HideLine(); hint.gameObject.SetActive(false);
                        title.text = cue.text; title.color = new Color(.88f, .86f, .78f, 0);
                        title.gameObject.SetActive(true);
                        float start = Time.unscaledTime;
                        while (Time.unscaledTime - start < cue.seconds)
                        {
                            Color color = title.color; color.a = Mathf.Clamp01((Time.unscaledTime - start) / cue.seconds);
                            title.color = color; yield return null;
                        }
                        title.color = new Color(.88f, .86f, .78f, 1);
                        atTitle = true; endingControls.SetActive(true);
                        foreach (var label in endingControls.GetComponentsInChildren<TextMeshProUGUI>(true)) {
                            label.ForceMeshUpdate(true);
                            log.Log("Ending choice label: " + label.text + "; characters=" + label.textInfo.characterCount + "; active=" + label.gameObject.activeInHierarchy + ".");
                        }
                        break;
                }
            }
        }

        private IEnumerable<object> ShowShop(BuybackCue cue)
        {
            doorAnchor = new Vector2(cue.x, cue.y); shopShade = cue.opacity;
            foreach (var frame in Fade(background, 0, shopShade, cue.seconds)) yield return frame;
        }
        private IEnumerable<object> SpeakLine(string text)
        {
            ShowLine(text);
            // Stage-local policy: the player's global auto-advance option stays untouched.
            while (!advanceRequested) yield return null;
            lineActive = false;
        }
        private IEnumerable<object> SpeakAttack(BuybackCue cue)
        {
            ShowLine(cue.text);
            bool revealed = false;
            while (!advanceRequested)
            {
                if (!revealed && Time.unscaledTime - lineStarted >= cue.seconds
                    && (shownCharacters >= cue.text.Length * cue.hold || !bubble.typewriter.isShowingText))
                {
                    SetImage(3); curtain.color = Color.clear; revealed = true;
                }
                yield return null;
            }
            if (!revealed) { SetImage(3); curtain.color = Color.clear; }
            lineActive = false;
        }
        private void Rechoose()
        {
            if (!atTitle || leaving) return;
            chooseAgain = true; retryDoor = 0;
            music.RetainUntilMorning();
            Play();
        }
        internal void LeaveForEnding() { leaving = true; Shutdown(); }
        internal void PlayFarewell(Action finished)
        {
            ModHelper.StopAllCoroutines(BuybackTestMod.Id);
            if (ModHelper.StartCoroutine(BuybackTestMod.Id, Guarded(Farewell(finished))) == null)
                throw new InvalidOperationException("Farewell coroutine did not start");
        }
        private IEnumerator Farewell(Action finished)
        {
            foreach (var frame in SpeakLine(refusalText.farewell_line)) yield return frame;
            finished();
        }

        private bool Reduced { get { return BuybackTestMod.Instance?.ReducedEffects?.Value ?? false; } }
        private void ResetView()
        {
            viewRoot.anchoredPosition = Vector2.zero;
            viewRoot.localRotation = Quaternion.identity; viewRoot.localScale = Vector3.one;
        }
        private void SetImage(int number)
        {
            cg.texture = textures[number - 1]; cg.enabled = true; cg.color = Color.white;
            imageStarted = Time.unscaledTime; LayoutImage();
        }
        private IEnumerable<object> ChangeImage(BuybackCue cue)
        {
            HideLine();
            if (cue.transition == "door_wipe")
            {
                foreach (var frame in Fade(background, shopShade, 1, .45f)) yield return frame;
                doorScene = false;
                SetImage(cue.value); curtain.color = Color.black;
                doorShade.Progress = 0; doorShade.gameObject.SetActive(true);
                foreach (var frame in Wait(cue.hold)) yield return frame;
                var door = EmporiumAudioPlayer.Instance?.airlock_ext_open;
                float duration = door != null && door.clip != null && door.clip.length > 0 ? door.clip.length : cue.seconds;
                curtain.color = Color.clear;
                Sound(door, .5f);
                log.Log("Door shadow: left to right; sound-synchronized duration=" + duration + ".");
                float started = Time.unscaledTime;
                while (Time.unscaledTime - started < duration)
                {
                    float t = Mathf.Clamp01((Time.unscaledTime - started) / duration);
                    doorShade.Progress = t * t * (3 - 2 * t);
                    yield return null;
                }
                doorShade.Progress = 1; doorShade.gameObject.SetActive(false);
            }
            else if (cue.transition == "dissolve")
            {
                previousCg.texture = cg.texture; previousCg.color = Color.white;
                previousCg.enabled = true; previousImageStarted = imageStarted;
                previousCg.rectTransform.sizeDelta = cg.rectTransform.sizeDelta;
                SetImage(cue.value); cg.color = new Color(1, 1, 1, 0);
                Sound(cue.value == 2 ? "blade" : "touch");
                float start = Time.unscaledTime;
                while (Time.unscaledTime - start < cue.seconds)
                {
                    float t = Mathf.Clamp01((Time.unscaledTime - start) / cue.seconds);
                    cg.color = new Color(1, 1, 1, t * t * (3 - 2 * t));
                    yield return null;
                }
                cg.color = Color.white; previousCg.enabled = false; previousCg.texture = null;
            }
            else if (cue.transition == "late_reveal")
            {
                if (cg.texture != textures[cue.value - 1]) SetImage(cue.value);
                curtain.color = Color.clear; ResetView();
            }
            else // Loss of consciousness: close, hold in black, reopen on floor POV.
            {
                Sound("fall");
                float start = Time.unscaledTime;
                while (Time.unscaledTime - start < .20f)
                {
                    float t = Mathf.Clamp01((Time.unscaledTime - start) / .20f);
                    curtain.color = new Color(0, 0, 0, t);
                    if (!Reduced)
                    {
                        viewRoot.anchoredPosition = new Vector2(0, -70 * t);
                        viewRoot.localRotation = Quaternion.Euler(0, 0, -3 * t);
                    }
                    yield return null;
                }
                curtain.color = Color.black;
                foreach (var frame in Wait(cue.hold)) yield return frame;
                SetImage(cue.value); ResetView();
                foreach (var frame in Fade(curtain, 1, 0, cue.seconds - .20f)) yield return frame;
            }
        }

        private void SetBloodBeat(int beat)
        {
            foreach (var graphic in bloodLayers) if (graphic != null) graphic.Beats = beat;
        }
        private IEnumerable<object> Impact(BuybackCue cue)
        {
            HideLine();
            foreach (var frame in Fade(curtain, 0, 1, cue.hold)) yield return frame;
            bloodStarted = Time.unscaledTime; lens.alpha = 1;
            float interval = cue.seconds / cue.value;
            for (int beat = 1; beat <= cue.value; beat++)
            {
                SetBloodBeat(beat);
                Sound("hit"); Sound("blood");
                float start = Time.unscaledTime;
                bool registered = false;
                while (Time.unscaledTime - start < interval)
                {
                    float elapsed = Time.unscaledTime - start;
                    float kick = Reduced ? 0 : Mathf.Clamp01(1 - elapsed / .18f);
                    var offset = new Vector2(Mathf.Sin(elapsed * 85) * 18 * kick,
                        Mathf.Sin(elapsed * 67) * 12 * kick);
                    viewRoot.anchoredPosition = lensRect.anchoredPosition = offset;
                    ambientLevel = .08f;
                    if (beat == cue.value && !registered && elapsed >= .24f)
                    {
                        Sound(EmporiumAudioPlayer.Instance?.audioRegister, .85f);
                        transaction.gameObject.SetActive(true); registered = true;
                    }
                    yield return null;
                }
                ResetView(); lensRect.anchoredPosition = Vector2.zero;
            }
            ambientLevel = .45f; transaction.gameObject.SetActive(false);
        }

        private static IEnumerable<object> Wait(float duration)
        {
            float until = Time.unscaledTime + duration;
            while (Time.unscaledTime < until) yield return null;
        }
        private static IEnumerable<object> Fade(Image image, float from, float to, float duration)
        {
            float start = Time.unscaledTime;
            do
            {
                float t = duration <= 0 ? 1 : Mathf.Clamp01((Time.unscaledTime - start) / duration);
                var color = Color.black; color.a = Mathf.Lerp(from, to, t);
                image.color = color;
                if (t >= 1) yield break;
                yield return null;
            } while (true);
        }
        private void ShowLine(string text)
        {
            HideLine(); advanceRequested = false; lineActive = true;
            shownCharacters = 0;
            lineStarted = Time.unscaledTime;
            speechRoot.gameObject.SetActive(true);
            bubble.textfield.fontSize = doorScene ? 20 : 28;
            speaker.fontSize = doorScene ? 18 : 26;
            // Same native formula, using the Chinese manuscript's CJK multiplier.
            bubble.typewriter.SetTypewriterSpeed((EmporiumOption.Instance.dialogSpeed * 2 + .2f) * .35f);
            bubble.typewriter.hideAppearancesOnSkip = false;
            bubble.typewriter.ShowText(text);
            if (!EmporiumOption.Instance.enableTypewriterEffect)
            {
                bubble.typewriter.hideAppearancesOnSkip = true;
                bubble.typewriter.SkipTypewriter();
            }
        }
        private void HideLine()
        {
            lineActive = false;
            if (bubble != null) bubble.typewriter.StopShowingText();
            if (speechRoot != null) speechRoot.gameObject.SetActive(false);
        }
        internal void Advance()
        {
            if (closing || leaving) return;
            if (waitingForKnock)
            {
                if (Time.frameCount <= knockPromptFrame || lastAdvanceFrame == Time.frameCount) return;
                lastAdvanceFrame = Time.frameCount;
                knockRequested = true; waitingForKnock = false;
                return;
            }
            if (!lineActive || Time.frameCount <= startedFrame + 1
                || lastAdvanceFrame == Time.frameCount || Time.unscaledTime - lineStarted < .12f) return;
            lastAdvanceFrame = Time.frameCount;
            if (bubble.typewriter.isShowingText) bubble.typewriter.SkipTypewriter();
            else advanceRequested = true;
        }
        private void Update()
        {
            if (closing) return;
            if (!leaving && KeyBindHelper.GetBackDown()) { Shutdown(); return; }
            if (!leaving && KeyBindHelper.GetKeyDown("advance_dialogue")) Advance();
            if (choiceMenu != null && choiceMenu.gameObject.activeSelf)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) SelectChoice(0);
                else if (Input.GetKeyDown(KeyCode.Alpha2)) SelectChoice(1);
            }
            if (ambience != null) ambience.volume = Mathf.Clamp01(EmporiumOption.Instance.backgroundVolume) * ambientLevel;
            if (bloodStarted >= 0 && lens.alpha > 0)
                drips.anchoredPosition = new Vector2(0, -Mathf.Min(18, (Time.unscaledTime - bloodStarted) * .7f));
            if (cg != null && cg.enabled) LayoutImage();
        }
        private void LateUpdate()
        {
            if (speechRoot == null || !speechRoot.gameObject.activeSelf) return;
            float scale = EmporiumOption.DialogBubbleScale;
            body.localScale = nameRect.localScale = Vector3.one * scale;
            float screenWidth = ((RectTransform)transform).rect.width;
            var width = body.GetComponent<DialogBubbleWidth>();
            if (width != null) width.maxWidth = Mathf.Min(doorScene ? 560 : 1000, screenWidth - 100) / scale - 16;
            nameRect.anchoredPosition = new Vector2(0, nameRect.rect.height * scale);
            if (doorScene) LayoutDoorScene();
            else
            {
                speechRoot.anchorMin = speechRoot.anchorMax = new Vector2(.5f, 0);
                float reserved = choiceMenu.gameObject.activeSelf ? choiceCount * ChoiceStep * scale + 14 : 0;
                speechRoot.anchoredPosition = new Vector2(-body.rect.width * scale / 2, 70 + reserved + body.rect.height * scale);
            }
            LayoutChoices(scale);
        }
        private void LayoutImage()
        {
            FitImage(cg, imageStarted);
            if (previousCg.enabled) FitImage(previousCg, previousImageStarted);
        }
        private void FitImage(RawImage image, float started)
        {
            if (image.texture == null) return;
            Rect rect = ((RectTransform)transform).rect;
            // Preserve original proportions and a margin for the small camera impulse.
            float fit = Mathf.Min(rect.width / image.texture.width, rect.height / image.texture.height) * .93f;
            float push = 1 + .02f * Mathf.Clamp01((Time.unscaledTime - started) / 45);
            Vector2 size = new Vector2(image.texture.width * fit * push, image.texture.height * fit * push);
            if (size != image.rectTransform.sizeDelta) image.rectTransform.sizeDelta = size;
        }
        private void LoadSounds(BuybackSound[] specs)
        {
            foreach (var spec in specs)
            {
                soundSpecs.Add(spec.role, spec);
                AudioClip unused; TrySound(spec.role, out unused);
            }
        }
        private bool TrySound(string role, out AudioClip clip)
        {
            if (soundClips.TryGetValue(role, out clip)) return clip != null;
            BuybackSound spec;
            if (!soundSpecs.TryGetValue(role, out spec)) return false;
            var directory = AudioManager.Exists ? AudioManager.Instance.audioDirectory : null;
            string key = spec.source == "mod" ? ModHelper.GetSoundIdentifier(BuybackTestMod.Id, spec.identifier) : spec.identifier;
            if (directory == null || !directory.HasAudioClip(key)) return false;
            clip = directory.GetAudioClip(key); soundClips[role] = clip;
            return clip != null;
        }
        private void Sound(string role, float scale = 1)
        {
            AudioClip clip; BuybackSound spec;
            if (!TrySound(role, out clip) || !soundSpecs.TryGetValue(role, out spec))
            { log.Warning("Cinematic sound unavailable: " + role); return; }
            effects.panStereo = spec.pan;
            effects.PlayOneShot(clip, Mathf.Clamp01(spec.gain * scale));
        }
        private void Sound(AudioSource source, float gain)
        {
            if (source == null || source.clip == null) return;
            effects.panStereo = 0;
            effects.PlayOneShot(source.clip, Mathf.Clamp01(source.volume * gain));
        }
        private void Mute(AudioSource source)
        {
            if (source == null || nativeMutes.ContainsKey(source)) return;
            nativeMutes.Add(source, source.mute); source.mute = true;
        }
        private void MuteNative()
        {
            if (AudioManager.Exists)
            {
                Mute(AudioManager.Instance.audioSourceAmbient);
            }
            if (StoreSoundPlayer.Instance != null)
            {
                if (!AudioManager.Exists || StoreSoundPlayer.Instance.musicAudioSource != AudioManager.Instance.audioSourceMusic)
                    Mute(StoreSoundPlayer.Instance.musicAudioSource);
                Mute(StoreSoundPlayer.Instance.ambianceAudioSource);
            }
        }
        internal void Shutdown()
        {
            if (closing) return;
            ModHelper.StopAllCoroutines(BuybackTestMod.Id);
            Release();
            gameObject.SetActive(false);
            UnityEngine.Object.Destroy(gameObject);
        }
        private void Release()
        {
            if (closing) return;
            closing = true; HideLine();
            if (ambience != null) ambience.Stop();
            if (effects != null) effects.Stop();
            music?.ReleaseStage(leaving);
            foreach (var pair in nativeMutes)
                if (pair.Key != null && pair.Key.mute) pair.Key.mute = pair.Value;
            nativeMutes.Clear();
            if (cg != null) cg.texture = null;
            if (previousCg != null) previousCg.texture = null;
            soundClips.Clear(); soundSpecs.Clear();
            for (int i = 0; i < textures.Length; i++)
            {
                if (textures[i] != null) UnityEngine.Object.Destroy(textures[i]);
                textures[i] = null;
            }
            onClosed?.Invoke(); onClosed = null;
        }
        private void OnDestroy() { Release(); }
    }
}
