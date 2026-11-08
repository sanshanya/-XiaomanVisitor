using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace XiaomanVisitor
{
    // Independent night actor. It never reads or replaces a native store client.
    internal sealed class XiaomanTavernStage : MonoBehaviour
    {
        private XiaomanMod mod;
        private RectTransform room;
        private RawImage background, actor, foreground, pickupPlate;
        private TextMeshProUGUI pickupText;
        private Texture2D backgroundTexture, foregroundTexture;
        private bool highDetail, hadRecording;
        private AudioSource channel;
        private AudioClip recording;
        private SpeechEnvelope envelope;
        private readonly Dictionary<AudioClip, SpeechEnvelope> envelopes = new Dictionary<AudioClip, SpeechEnvelope>();
        private TextMeshProUGUI caption;
        private string sentence = "";
        private Mood authored;
        private float lineStart, nextBlink, blinkStart = -100f;

        internal void Configure(XiaomanMod owner, RectTransform parent)
        {
            mod = owner;
            var root = (RectTransform)transform;
            root.SetParent(parent, false);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero; root.offsetMax = Vector2.zero;
            root.SetAsFirstSibling();
            room = new GameObject("Room", typeof(RectTransform)).GetComponent<RectTransform>();
            room.SetParent(root, false);
            room.anchorMin = room.anchorMax = room.pivot = new Vector2(.5f, .5f);
            backgroundTexture = ReadTexture("background.png", 1672, 941);
            foregroundTexture = ReadTexture("bar-occluder.png", 384, 48);
            background = Image("Background", backgroundTexture);
            bool detailed;
            Texture2D portraits = mod.TavernPortraitAtlas(out detailed);
            if (portraits == null) throw new InvalidOperationException("Portrait assets are not ready");
            highDetail = detailed;
            actor = Image("Xiaoman", portraits);
            foreground = Image("Bar", foregroundTexture);
            pickupPlate = Image("PickupSign", Texture2D.whiteTexture);
            pickupPlate.color = new Color32(225, 216, 183, 255);
            pickupText = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            pickupText.transform.SetParent(pickupPlate.transform, false);
            pickupText.rectTransform.anchorMin = Vector2.zero; pickupText.rectTransform.anchorMax = Vector2.one;
            pickupText.rectTransform.offsetMin = pickupText.rectTransform.offsetMax = Vector2.zero;
            pickupText.alignment = TextAlignmentOptions.Center;
            pickupText.color = new Color32(40, 70, 107, 255); pickupText.raycastTarget = false;
            pickupPlate.gameObject.SetActive(false);
            channel = gameObject.AddComponent<AudioSource>();
            channel.playOnAwake = false; channel.loop = false; channel.spatialBlend = 0;
            nextBlink = Time.unscaledTime + 3f;
            Layout();
            Pose(Mood.Neutral, 0);
        }

        private Texture2D ReadTexture(string file, int width, int height)
        {
            byte[] bytes = ModHelper.ReadModFileBytes(XiaomanMod.Id, TavernRules.SceneFolder + file);
            if (bytes == null) throw new InvalidOperationException("Missing tavern " + file);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, true) || texture.width != width || texture.height != height)
            { UnityEngine.Object.Destroy(texture); throw new InvalidOperationException("Invalid tavern " + file); }
            texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp; texture.anisoLevel = 0;
            return texture;
        }

        private RawImage Image(string name, Texture texture)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
            image.transform.SetParent(room, false);
            image.texture = texture; image.raycastTarget = false;
            return image;
        }

        private void Layout()
        {
            if (room == null || room.parent == null) return;
            Rect rect = ((RectTransform)room.parent).rect;
            float scale = (float)TavernRules.FitScale(rect.width, rect.height);
            room.sizeDelta = new Vector2(5016 * scale, 2823 * scale);
            Place(background, 0, 0, 5016, 2823, scale);
            Place(actor, 2628, 1004, 384, 688, scale);
            Place(foreground, 2628, 1644, 384, 48, scale);
            Place(pickupPlate, 3430, 1570, 280, 74, scale);
            if (pickupText != null) pickupText.fontSize = 36 * scale;
        }

        private static void Place(RawImage image, float x, float y, float w, float h, float scale)
        {
            if (image == null) return;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x * scale, -y * scale);
            rect.sizeDelta = new Vector2(w * scale, h * scale);
        }

        internal void AttachCaption(TextMeshProUGUI text)
        {
            caption = text;
            if (pickupText != null && text != null) pickupText.font = text.font;
        }

        internal void SetPickupPoint(bool ready, string text)
        {
            if (pickupPlate == null) return;
            pickupPlate.gameObject.SetActive(ready);
            if (caption != null && pickupText.font != caption.font) pickupText.font = caption.font;
            pickupText.text = text;
        }

        internal void Say(string text, Mood mood, VoiceRecording voice)
        {
            StopVoice();
            sentence = text ?? ""; authored = mood; lineStart = Time.unscaledTime;
            if (caption != null) { caption.text = sentence; caption.maxVisibleCharacters = 0; }
            AudioClip clip = voice == null ? null : mod.VoiceClip(voice);
            if (clip == null || !mod.VoiceEnabled.Value || !AudioManager.Exists) return;
            recording = clip; hadRecording = true;
            if (!envelopes.TryGetValue(clip, out envelope))
            {
                try {
                    float[] samples = new float[checked(clip.samples * clip.channels)];
                    if (clip.GetData(samples, 0)) envelope = SpeechEnvelope.FromSamples(samples, clip.frequency, clip.channels);
                }
                catch (Exception error) { mod.Log.Warning("Tavern lip fallback: " + error.Message); }
                envelopes[clip] = envelope;
            }
            AudioSource reference = AudioManager.Instance.audioSource3;
            if (reference != null) channel.outputAudioMixerGroup = reference.outputAudioMixerGroup;
            ApplyVoiceVolume(); channel.clip = clip; channel.Play();
        }

        private void ApplyVoiceVolume()
        {
            float volume = 1f;
            if (AudioManager.Exists && AudioManager.Instance.audioSource3 != null) volume = AudioManager.Instance.audioSource3.volume;
            channel.volume = volume * Mathf.Clamp01(mod.VoiceVolume.Value);
        }

        private void Pose(Mood mood, int mouth)
        {
            int index = TavernRules.FrameIndex(mood, mouth);
            int columns = highDetail ? 6 : 3, rows = highDetail ? 3 : 6;
            actor.uvRect = new Rect((index % columns) / (float)columns,
                1f - (index / columns + 1f) / rows, 1f / columns, 1f / rows);
        }

        private void LateUpdate()
        {
            if (mod == null || actor == null) return;
            Layout();
            float now = Time.unscaledTime;
            int revealed = Math.Min(sentence.Length, Math.Max(0, (int)((now - lineStart) * 30f)));
            if (caption != null) caption.maxVisibleCharacters = revealed;
            if (channel != null && channel.isPlaying && !mod.VoiceEnabled.Value) channel.Stop();
            bool busy = channel != null && recording != null && channel.clip == recording && channel.isPlaying;
            int mouth = 0;
            if (busy) {
                ApplyVoiceVolume();
                if (!AudioListener.pause && !channel.mute && channel.volume > 0)
                    mouth = envelope == null ? PortraitTiming.Cadence(channel.time) : envelope.MouthAt(channel.time);
            }
            DialoguePose pose = mod.Animate.Value
                ? PortraitTiming.Resolve(caption != null, true, authored, hadRecording, busy,
                    revealed < sentence.Length, mouth, now - lineStart)
                : new DialoguePose(Mood.Neutral, 0, false);
            Mood expression = pose.Expression;
            if (mod.Animate.Value) {
                if (now >= nextBlink) { blinkStart = now; nextBlink = now + UnityEngine.Random.Range(3f, 5.8f); }
                float phase = now - blinkStart;
                if (PortraitTiming.MayBlink(expression) && phase >= 0 && phase < .24f)
                    expression = phase < .06f || phase > .18f ? Mood.Soft : Mood.Closed;
            }
            Pose(expression, pose.Mouth);
        }

        internal void EndSession()
        {
            StopVoice(); caption = null; sentence = "";
            if (actor != null) Pose(Mood.Neutral, 0);
        }
        private void StopVoice()
        {
            if (channel != null) { channel.Stop(); channel.clip = null; }
            recording = null; envelope = null; hadRecording = false;
        }
        private void OnDisable() { EndSession(); }
        private void OnDestroy()
        {
            StopVoice(); envelopes.Clear();
            if (backgroundTexture != null) UnityEngine.Object.Destroy(backgroundTexture);
            if (foregroundTexture != null) UnityEngine.Object.Destroy(foregroundTexture);
        }
    }
}
