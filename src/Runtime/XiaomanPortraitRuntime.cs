using System;
using System.Collections.Generic;
using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private bool PreparePortraitAssets(LoadedMod mod)
        {
            if (mod.Manifest.ID != Id) return false;
            Texture2D loaded;
            if (!mod.Textures.TryGetValue("xiaoman_atlas", out loaded))
            {
                Log.Error("Missing Textures/xiaoman_atlas.png; visitor generation is disabled.");
                return false;
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
            return true;
        }

        private void DestroySprites()
        {
            DestroyGunSprites();
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

        private void SetMood(StoreClient client, Mood expression) { moodOwner = client; mood = expression; }
        internal Mood GetMood(StoreClient client) { return client == moodOwner ? mood : Mood.Neutral; }
        internal Sprite Frame(Mood expression, int mouth) { return frames[(int)expression, mouth]; }
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
            mod.TickBrewing();
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
            if (client == null || !GunReaction.Protects(true,client.identifier))
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
            DialogUIManager dialog = DialogUIManager.Instance;
            Dialogue line = dialog == null ? null : dialog.currentDialog;
            bool visible = line != null && line.title == client.displayName
                && dialog.clientBubble != null && dialog.clientBubble.gameObject.activeInHierarchy;
            bool typing = visible && dialog.clientBubble.typewriter != null && dialog.clientBubble.typewriter.isShowingText;
            int voiceMouth = 0;
            bool voiceBusy = mod.Voice != null && mod.Voice.PoseFor(line, out voiceMouth);
            bool hadVoice = mod.Voice != null && mod.Voice.HasPlayedFor(line);
            if (mod.Animate.Value)
            {
                Mood authored;
                bool canSpeak;
                if (!mod.TryLinePerformance(line, out authored, out canSpeak))
                { authored = Mood.Neutral; canSpeak = true; }
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
            Sprite frame = mod.GunPortrait(client, line, visible, typing, hadVoice, voiceBusy, voiceMouth, mouth)
                ?? mod.Frame(expression, mouth);
            if (frame != null && renderer.sprite != frame) renderer.sprite = frame;
        }
    }
}
