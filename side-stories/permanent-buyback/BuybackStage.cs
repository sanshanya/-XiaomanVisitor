using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace XiaomanVisitor.SideStories
{
    [Serializable] internal sealed class BuybackScript { public int schema; public string language; public BuybackCue[] cues; }
    [Serializable] internal sealed class BuybackCue
    { public string kind, text, key; public int value, act; public float seconds; }
    [Serializable] internal sealed class BuybackAssets { public int schema; public BuybackImage[] images; }
    [Serializable] internal sealed class BuybackImage { public string file; public int width, height; public string sha256; }

    internal sealed class BuybackStage : MonoBehaviour
    {
        private readonly Texture2D[] textures = new Texture2D[5];
        private readonly Dictionary<AudioSource, bool> nativeMutes = new Dictionary<AudioSource, bool>();
        private BuybackScript script;
        private ModLog log;
        private Action onClosed;
        private Canvas canvas;
        private RectTransform speechRoot, body, nameRect;
        private TextBubble bubble;
        private TextMeshProUGUI speaker, transaction, title, hint;
        private GameObject endingControls;
        private RawImage cg;
        private Image background, curtain, flash;
        private AudioSource ambience, effects;
        private bool closing, lineActive, advanceRequested, atTitle, leaving;
        private float completedAt = -1, imageStarted, lineStarted;
        private int startedFrame, lastAdvanceFrame = -1;
        private Vector2 lastSize;

        internal void Prepare(ModLog ownerLog, Action closed)
        {
            log = ownerLog; onClosed = closed;
            script = JsonUtility.FromJson<BuybackScript>(Read("script.json"));
            var assets = JsonUtility.FromJson<BuybackAssets>(Read("assets.json"));
            Validate(script, assets);
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
        }

        private static string Read(string file)
        {
            string text = ModHelper.ReadModFileText(BuybackTestMod.Id, "Scenes/" + file);
            if (string.IsNullOrEmpty(text)) throw new InvalidOperationException("Missing " + file);
            return text;
        }

        internal static void Validate(BuybackScript story, BuybackAssets assets)
        {
            if (story == null || story.schema != 1 || story.language != "zh" || story.cues == null
                || story.cues.Length < 100 || story.cues.Length > 250
                || assets == null || assets.schema != 1 || assets.images == null || assets.images.Length != 5)
                throw new InvalidOperationException("Invalid five-act rehearsal data");
            for (int i = 0; i < 5; i++)
            {
                var image = assets.images[i];
                if (image == null || image.file != "cg_0" + (i + 1) + ".png"
                    || image.width < 512 || image.width > 4096 || image.height < 512 || image.height > 4096
                    || image.sha256 == null || image.sha256.Length != 64)
                    throw new InvalidOperationException("Invalid CG manifest");
            }
            int nextImage = 1, previousAct = 1;
            bool impact = false, black = false, title = false;
            foreach (var cue in story.cues)
            {
                if (cue == null || title || cue.seconds < 0 || cue.seconds > 10 || float.IsNaN(cue.seconds))
                    throw new InvalidOperationException("Invalid cue timing/order");
                switch (cue.kind)
                {
                    case "line":
                        if (string.IsNullOrEmpty(cue.text) || cue.text.Length > 200
                            || cue.act < previousAct || cue.act > previousAct + 1 || cue.act > 5)
                            throw new InvalidOperationException("Invalid spoken line/act order");
                        previousAct = cue.act;
                        break;
                    case "image":
                        if (cue.value != nextImage++) throw new InvalidOperationException("Invalid CG order");
                        break;
                    case "impact":
                        if (nextImage != 4 || impact) throw new InvalidOperationException("Impact before CG03");
                        impact = true; break;
                    case "black":
                        if (nextImage != 6 || black) throw new InvalidOperationException("Final fade before CG05");
                        black = true; break;
                    case "title": title = true; break;
                    case "wait": case "fade": case "knock": case "lock": case "tape": break;
                    default: throw new InvalidOperationException("Unknown rehearsal cue");
                }
            }
            if (nextImage != 6 || previousAct != 5 || !impact || !black || !title)
                throw new InvalidOperationException("Incomplete ending");
        }

        private void BuildPresentation()
        {
            var native = DialogUIManager.Instance;
            if (native == null || native.clientBubble == null || native.clientNameBubble == null)
                throw new InvalidOperationException("Native dialogue templates are not ready");
            canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1;
            background = FullImage("FadeFromShop", Color.clear);
            background.raycastTarget = true;
            ClickAdvance(background.gameObject);
            cg = Child("CG").gameObject.AddComponent<RawImage>();
            cg.raycastTarget = false; cg.enabled = false;
            curtain = FullImage("Transition", Color.clear);
            flash = FullImage("Impact", Color.clear);
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
            bubble.typewriter.onTextShowed.AddListener(() => completedAt = Time.unscaledTime);
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
            endingControls = Child("EndingControls").gameObject;
            endingControls.SetActive(false);
            AddButton("重播", new Vector2(-150, -80), Play);
            AddButton("返回主菜单", new Vector2(150, -80), ReturnToMenu);
            ambience = gameObject.AddComponent<AudioSource>();
            ambience.playOnAwake = false; ambience.loop = true; ambience.spatialBlend = 0;
            effects = gameObject.AddComponent<AudioSource>();
            effects.playOnAwake = false; effects.spatialBlend = 0;
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
            var rect = Child(imageName); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false;
            return image;
        }
        private TextMeshProUGUI Label(string labelName, Vector2 position, float size, Transform parent = null)
        {
            var rect = Child(labelName, parent);
            rect.sizeDelta = new Vector2(1250, 90); rect.anchoredPosition = position;
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = DialogUIManager.Instance.clientBubble.textfield.font;
            text.fontSize = size; text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false; text.color = new Color(.88f, .86f, .78f, 1);
            return text;
        }
        private void AddButton(string label, Vector2 position, Action click)
        {
            var rect = Child(label, endingControls.transform);
            rect.sizeDelta = new Vector2(270, 65); rect.anchoredPosition = position;
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.12f, .1f, .16f, 1);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => click());
            var text = Label("Label", Vector2.zero, 26, rect);
            text.rectTransform.sizeDelta = rect.sizeDelta; text.text = label;
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
            cg.enabled = false; curtain.color = flash.color = background.color = Color.clear;
            hint.gameObject.SetActive(true); startedFrame = Time.frameCount; lastAdvanceFrame = -1;
            InputActionManager.current?.ResetExcept(null);
            MuteNative();
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
            foreach (var cue in script.cues)
            {
                switch (cue.kind)
                {
                    case "fade":
                        foreach (var frame in Fade(background, 0, 1, cue.seconds)) yield return frame;
                        break;
                    case "knock":
                        for (int i = 0; i < 3; i++)
                        {
                            Sound(EmporiumAudioPlayer.Instance?.ratchet, .32f);
                            foreach (var frame in Wait(cue.seconds / 3)) yield return frame;
                        }
                        break;
                    case "image":
                        HideLine();
                        if (cue.value == 1) Sound(EmporiumAudioPlayer.Instance?.airlock_ext_open, .5f);
                        foreach (var frame in Fade(curtain, 0, 1, cue.seconds / 2)) yield return frame;
                        cg.texture = textures[cue.value - 1]; cg.enabled = true; imageStarted = Time.unscaledTime;
                        LayoutImage();
                        foreach (var frame in Fade(curtain, 1, 0, cue.seconds / 2)) yield return frame;
                        break;
                    case "line":
                        ShowLine(cue.text);
                        while (completedAt < 0 || (!advanceRequested &&
                            (!EmporiumOption.Instance.autoNextDialog || Time.unscaledTime - completedAt <
                                DialogUIManager.ScaleByDialogSpeed(.99f) + DialogUIManager.GetTypewriterDelay()))) yield return null;
                        lineActive = false;
                        break;
                    case "wait":
                        foreach (var frame in Wait(cue.seconds)) yield return frame;
                        break;
                    case "impact":
                        HideLine(); Sound(EmporiumAudioPlayer.Instance?.ratchet2, .75f);
                        foreach (var frame in Fade(flash, 0, .92f, .07f, true)) yield return frame;
                        Sound(EmporiumAudioPlayer.Instance?.audioRegister, .85f);
                        transaction.gameObject.SetActive(true);
                        foreach (var frame in Fade(flash, .92f, 0, .2f, true)) yield return frame;
                        foreach (var frame in Wait(cue.seconds)) yield return frame;
                        transaction.gameObject.SetActive(false);
                        break;
                    case "black":
                        HideLine();
                        foreach (var frame in Fade(curtain, 0, 1, cue.seconds)) yield return frame;
                        cg.enabled = false;
                        break;
                    case "lock":
                        Sound(EmporiumAudioPlayer.Instance?.airlock_ext_close, .45f);
                        foreach (var frame in Wait(cue.seconds)) yield return frame;
                        break;
                    case "tape":
                        Sound(EmporiumAudioPlayer.Instance?.audioClickClick, .4f);
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
                        break;
                }
            }
        }

        private static IEnumerable<object> Wait(float duration)
        {
            float until = Time.unscaledTime + duration;
            while (Time.unscaledTime < until) yield return null;
        }
        private static IEnumerable<object> Fade(Image image, float from, float to, float duration, bool white = false)
        {
            float start = Time.unscaledTime;
            do
            {
                float t = duration <= 0 ? 1 : Mathf.Clamp01((Time.unscaledTime - start) / duration);
                var color = white ? Color.white : Color.black; color.a = Mathf.Lerp(from, to, t);
                image.color = color;
                if (t >= 1) yield break;
                yield return null;
            } while (true);
        }
        private void ShowLine(string text)
        {
            HideLine(); advanceRequested = false; completedAt = -1; lineActive = true;
            lineStarted = Time.unscaledTime;
            speechRoot.gameObject.SetActive(true);
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
            if (!lineActive || leaving || Time.frameCount <= startedFrame + 1
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
            if (ambience != null) ambience.volume = Mathf.Clamp01(EmporiumOption.Instance.backgroundVolume) * .7f;
            if (cg != null && cg.enabled) LayoutImage();
        }
        private void LateUpdate()
        {
            if (speechRoot == null || !speechRoot.gameObject.activeSelf) return;
            float scale = EmporiumOption.DialogBubbleScale;
            body.localScale = nameRect.localScale = Vector3.one * scale;
            float screenWidth = ((RectTransform)transform).rect.width;
            var width = body.GetComponent<DialogBubbleWidth>();
            if (width != null) width.maxWidth = Mathf.Min(1000, screenWidth - 100) / scale - 16;
            nameRect.anchoredPosition = new Vector2(0, nameRect.rect.height * scale);
            speechRoot.anchoredPosition = new Vector2(-body.rect.width * scale / 2, 70 + body.rect.height * scale);
        }
        private void LayoutImage()
        {
            if (cg.texture == null) return;
            Rect rect = ((RectTransform)transform).rect;
            // Leave a 5% safety margin: the subtle push never crops the supplied originals.
            float fit = Mathf.Min(rect.width / cg.texture.width, rect.height / cg.texture.height) * .95f;
            float push = 1 + .02f * Mathf.Clamp01((Time.unscaledTime - imageStarted) / 45);
            Vector2 size = new Vector2(cg.texture.width * fit * push, cg.texture.height * fit * push);
            if (size != lastSize) { cg.rectTransform.sizeDelta = size; lastSize = size; }
        }
        private void Sound(AudioSource source, float gain)
        {
            if (source == null || source.clip == null) return;
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
                Mute(AudioManager.Instance.audioSourceMusic); Mute(AudioManager.Instance.audioSourceAmbient);
            }
            if (StoreSoundPlayer.Instance != null)
            {
                Mute(StoreSoundPlayer.Instance.musicAudioSource); Mute(StoreSoundPlayer.Instance.ambianceAudioSource);
            }
        }
        private void ReturnToMenu()
        {
            if (!atTitle || leaving || StoreUIManager.Instance == null) return;
            leaving = true; endingControls.SetActive(false);
            // Native navigation resets only live state. Do not call ExecuteGameOver/RunStats or save.
            StoreUIManager.Instance.escapeUIManager.OnMainMenu();
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
            foreach (var pair in nativeMutes)
                if (pair.Key != null && pair.Key.mute) pair.Key.mute = pair.Value;
            nativeMutes.Clear();
            if (cg != null) cg.texture = null;
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
