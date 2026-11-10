using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace XiaomanVisitor
{
    // Independent night actor. It never reads or replaces a native store client.
    [DefaultExecutionOrder(1000)]
    internal sealed class XiaomanTavernStage : MonoBehaviour
    {
        private XiaomanMod mod;
        private RectTransform room;
        internal RectTransform ControlRoot { get;private set; }
        private RawImage background, actor, foreground;
        private RectTransform actorHit, rainHit, tapeHit;
        private RectTransform workBar, workFill;
        private TextMeshProUGUI workLabel;
        private Texture2D backgroundTexture, foregroundTexture;
        private bool highDetail, hadRecording;
        private AudioSource channel, music, suppressedMusic;
        private bool previousMusicMute;
        private AudioClip recording;
        private SpeechEnvelope envelope;
        private readonly Dictionary<AudioClip, SpeechEnvelope> envelopes = new Dictionary<AudioClip, SpeechEnvelope>();
        private XiaomanTavernBubble speech;
        private XiaomanTavernChoices choices;
        private readonly Vector3[] bagCorners=new Vector3[4];
        private RectTransform actionPanel;
        private Mood authored;
        private float lineStart, nextBlink, blinkStart = -100f;
        private Vector2 layoutSize = new Vector2(-1f, -1f);
        private Vector2 actorRest;
        private float idleStarted, roomScale;
        private float conversationBottom;
        internal bool Speaking { get { return speech!=null && speech.Visible; } }
        internal bool ChoicesVisible { get { return choices!=null && choices.Visible; } }

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
            backgroundTexture = ReadTexture("background.png", TavernSceneLayout.ContentWidth, TavernSceneLayout.ContentHeight);
            foregroundTexture = ReadTexture("bar-occluder.png", TavernSceneLayout.BarWidth, TavernSceneLayout.BarHeight);
            background = Image("Background", backgroundTexture);
            bool detailed;
            Texture2D portraits = mod.SharedPortraitAtlas(out detailed);
            if (portraits == null) throw new InvalidOperationException("Portrait assets are not ready");
            highDetail = detailed;
            actor = Image("Xiaoman", portraits);
            foreground = Image("Bar", foregroundTexture);
            var ambient = gameObject.AddComponent<XiaomanTavernAmbient>();
            ambient.Configure(owner, background, foreground);
            actor.gameObject.AddComponent<XiaomanTavernLighting>().Configure(owner, ambient);
            rainHit=Hotspot("Rain",() => mod.ClickTavernSpot("rain"));
            tapeHit=Hotspot("Tape",() => mod.ClickTavernSpot("tape"));
            actorHit=Hotspot("Actor",() => mod.ClickTavernSpot("actor"));
            channel = gameObject.AddComponent<AudioSource>();
            channel.playOnAwake = false; channel.loop = false; channel.spatialBlend = 0;
            music=gameObject.AddComponent<AudioSource>();
            music.playOnAwake=false; music.loop=true; music.spatialBlend=0;
            speech = new XiaomanTavernBubble(room, StopVoice);
            choices = new XiaomanTavernChoices(room);
            nextBlink = Time.unscaledTime + 3f;
            ControlRoot=new GameObject("Xiaoman.TavernControls",typeof(RectTransform)).GetComponent<RectTransform>();
            ControlRoot.SetParent(room,false);ControlRoot.anchorMin=ControlRoot.anchorMax=ControlRoot.pivot=new Vector2(1,0);
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

        private RectTransform Hotspot(string name,Action click)
        {
            var area=new GameObject(name+".Hit",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(Button));
            var rect=(RectTransform)area.transform; rect.SetParent(room,false);
            var image=area.GetComponent<Image>(); image.color=Color.clear; image.raycastTarget=true;
            var button=area.GetComponent<Button>(); button.targetGraphic=image; button.transition=Selectable.Transition.None;
            button.onClick.AddListener(() => click());
            return rect;
        }

        private void Layout()
        {
            if (room == null || room.parent == null) return;
            Rect rect = ((RectTransform)room.parent).rect;
            if (rect.size == layoutSize) return;
            layoutSize = rect.size;
            float scale = (float)TavernRules.FitScale(rect.width, rect.height);
            roomScale = scale;
            room.sizeDelta = new Vector2(TavernSceneLayout.CanvasWidth * scale, TavernSceneLayout.CanvasHeight * scale);
            Place(background, 0, 0, TavernSceneLayout.CanvasWidth, TavernSceneLayout.CanvasHeight, scale);
            Place(actor, TavernSceneLayout.ActorX, TavernSceneLayout.ActorY, TavernSceneLayout.ActorWidth, TavernSceneLayout.ActorHeight, scale);
            actorRest = actor.rectTransform.anchoredPosition;
            Place(foreground, TavernSceneLayout.BarX, TavernSceneLayout.BarY, TavernSceneLayout.BarWidth, TavernSceneLayout.BarHeight, scale);
            PlaceHit(actorHit,TavernSceneLayout.ActorX,TavernSceneLayout.ActorY,TavernSceneLayout.ActorWidth,TavernSceneLayout.ActorHeight,scale);
            PlaceHit(rainHit,TavernSceneLayout.RainX,TavernSceneLayout.RainY,TavernSceneLayout.RainWidth,TavernSceneLayout.RainHeight,scale);
            PlaceHit(tapeHit,TavernSceneLayout.TapeX,TavernSceneLayout.TapeY,TavernSceneLayout.TapeWidth,TavernSceneLayout.TapeHeight,scale);
            if (ControlRoot!=null) {
                float unit=room.rect.height/1080f;ControlRoot.localScale=Vector3.one*unit;
                ControlRoot.sizeDelta=new Vector2(270,40);ControlRoot.anchoredPosition=new Vector2(-18*unit,18*unit);
            }
            LayoutWorkBar();
        }

        private static void Place(RawImage image, float x, float y, float w, float h, float scale)
        {
            if (image == null) return;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x * scale, -y * scale);
            rect.sizeDelta = new Vector2(w * scale, h * scale);
        }

        private static void PlaceHit(RectTransform rect,float x,float y,float w,float h,float scale)
        {
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=new Vector2(x*scale,-y*scale); rect.sizeDelta=new Vector2(w*scale,h*scale);
        }
        private RectTransform WorkImage(string name,Transform parent,Color color)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            var rect=(RectTransform)go.transform;rect.SetParent(parent,false);
            var image=go.GetComponent<Image>();image.color=color;image.raycastTarget=false;
            return rect;
        }
        internal void SetWorkProgress(float progress,string label)
        {
            if (workBar==null) {
                workBar=WorkImage("Xiaoman.WorkProgress",room,new Color32(43,37,58,240));
                var track=WorkImage("Track",workBar,new Color32(76,67,88,255));
                track.anchorMin=new Vector2(.04f,.1f);track.anchorMax=new Vector2(.96f,.3f);track.offsetMin=track.offsetMax=Vector2.zero;
                workFill=WorkImage("Fill",track,new Color32(146,205,209,255));
                workFill.anchorMin=Vector2.zero;workFill.anchorMax=Vector2.one;workFill.offsetMin=workFill.offsetMax=Vector2.zero;
                var go=new GameObject("Label",typeof(RectTransform),typeof(CanvasRenderer),typeof(TextMeshProUGUI));
                workLabel=go.GetComponent<TextMeshProUGUI>();workLabel.transform.SetParent(workBar,false);
                workLabel.rectTransform.anchorMin=new Vector2(.04f,.4f);workLabel.rectTransform.anchorMax=new Vector2(.96f,.98f);
                workLabel.rectTransform.offsetMin=workLabel.rectTransform.offsetMax=Vector2.zero;
                workLabel.font=DialogUIManager.Instance.clientNameTMP.font;workLabel.alignment=TextAlignmentOptions.Center;
                workLabel.color=new Color32(226,224,197,255);workLabel.raycastTarget=false;
                if (ControlRoot!=null) {
                float unit=room.rect.height/1080f;ControlRoot.localScale=Vector3.one*unit;
                ControlRoot.sizeDelta=new Vector2(270,40);ControlRoot.anchoredPosition=new Vector2(18*unit,18*unit);
            }
            LayoutWorkBar();
            }
            workBar.gameObject.SetActive(true);
            workFill.anchorMax=new Vector2(Mathf.Clamp01(progress),1);workLabel.text=label;
        }
        private void LayoutWorkBar()
        {
            if (workBar==null) return;
            PlaceHit(workBar,TavernSceneLayout.ActorX+TavernSceneLayout.ActorWidth*.5f-550,
                TavernSceneLayout.BarY+TavernSceneLayout.BarHeight+75,1100,180,roomScale);
            workLabel.fontSize=50*roomScale;
        }
        internal void HideWorkProgress() { if (workBar!=null) workBar.gameObject.SetActive(false); }

        internal void AttachPanel(RectTransform panel) { actionPanel = panel; }
        internal void ShowChoices(TavernChoice[] options,Action onDismiss) { choices.Show(options,onDismiss); }
        internal void HideChoices() { choices?.Hide(); }
        private float ConversationBottom()
        {
            var bag=EmporiumEntry.Instance?.afterhourWindow;
            if (bag==null || bag.handler==null || !bag.handler.gameObject.activeInHierarchy) return room.rect.height;
            bag.rectTransform.GetWorldCorners(bagCorners);
            return Mathf.Clamp(room.rect.height*.5f-room.InverseTransformPoint(bagCorners[1]).y,32,room.rect.height);
        }
        internal void SetSpeechSpeed(float speed) { speech?.SetSpeed(speed); }
        internal bool AdvanceSpeech()
        { if (speech == null || !speech.Visible) return false; speech.Advance(); return true; }
        internal void CancelSpeech() { StopVoice(); speech?.Hide(); }

        internal bool PlayMusic(string key)
        {
            var track=CassetteCollection.Find(key);
            if (track==null || !AudioManager.Exists || music==null) return false;
            AudioClip clip=AudioManager.Instance.audioDirectory.GetAudioClip(ModHelper.GetSoundIdentifier(XiaomanMod.Id,track.SoundKey));
            if (clip==null) return false;
            if (suppressedMusic==null) {
                suppressedMusic=AudioManager.Instance.audioSourceMusic;
                if (suppressedMusic!=null) { previousMusicMute=suppressedMusic.mute; suppressedMusic.mute=true; }
            }
            var reference=AudioManager.Instance.audioSourceMusic;
            if (reference!=null) { music.outputAudioMixerGroup=reference.outputAudioMixerGroup; music.pitch=reference.pitch; }
            music.volume=Mathf.Clamp01(EmporiumOption.Instance.musicVolume);
            music.clip=clip; music.mute=previousMusicMute; music.Play(); return true;
        }
        internal void PauseMusic() { music?.Pause(); }
        internal void ResumeMusic() { music?.UnPause(); }
        private void StopMusic()
        {
            if (music!=null) { music.Stop(); music.clip=null; }
            // Keep the native cassette coroutine/playlist running; only own its temporary mute.
            if (suppressedMusic!=null && suppressedMusic.mute) suppressedMusic.mute=previousMusicMute;
            suppressedMusic=null;
        }

        internal void Say(string speaker, string text, Mood mood, VoiceRecording voice)
        {
            StopVoice();
            authored = mood; lineStart = Time.unscaledTime;
            speech.Show(speaker, text ?? "");
            StoreUIManager.Instance.SetTypewriterSpeed();
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

        private void Update()
        {
            if (mod == null || actor == null) return;
            Layout();
            conversationBottom=ConversationBottom();
            speech.PrepareLayout(actionPanel==ControlRoot?null:actionPanel);
        }

        private void LateUpdate()
        {
            if (mod == null || actor == null) return;
            float now = Time.unscaledTime;
            actor.rectTransform.anchoredPosition = actorRest + Vector2.up * (roomScale * TavernRules.IdleLift(now - idleStarted, mod.Animate.Value));
            if (channel != null && channel.isPlaying && !mod.VoiceEnabled.Value) channel.Stop();
            bool busy = channel != null && recording != null && channel.clip == recording && channel.isPlaying;
            if (music!=null)
                music.volume=Mathf.Clamp01(EmporiumOption.Instance.musicVolume)*(busy ? .4f : 1f);
            speech.Tick(actor.rectTransform, busy, mod.VoiceWait.Value, actorRest,conversationBottom);
            choices.Tick(actor.rectTransform,actorRest,conversationBottom,speech.OccupiedBounds);
            int mouth = 0;
            if (busy) {
                ApplyVoiceVolume();
                if (!AudioListener.pause && !channel.mute && channel.volume > 0)
                    mouth = envelope == null ? PortraitTiming.Cadence(channel.time) : envelope.MouthAt(channel.time);
            }
            DialoguePose pose = mod.Animate.Value
                ? PortraitTiming.Resolve(speech.Visible, true, authored, hadRecording, busy,
                    speech.Typing, mouth, now - lineStart)
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
            StopVoice(); StopMusic(); speech?.Hide(); choices?.Hide(); HideWorkProgress(); actionPanel = null;
            if (actor != null) { actor.rectTransform.anchoredPosition = actorRest; Pose(Mood.Neutral, 0); }
        }
        private void StopVoice()
        {
            if (channel != null) { channel.Stop(); channel.clip = null; }
            recording = null; envelope = null; hadRecording = false;
        }
        private void OnEnable() { idleStarted = Time.unscaledTime; }
        private void OnDisable() { EndSession(); }
        private void OnDestroy()
        {
            StopVoice(); StopMusic(); envelopes.Clear();
            if (backgroundTexture != null) UnityEngine.Object.Destroy(backgroundTexture);
            if (foregroundTexture != null) UnityEngine.Object.Destroy(foregroundTexture);
        }
    }
}
