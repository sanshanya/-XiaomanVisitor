using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private sealed class VoiceBinding
        {
            public string Key;
            public string Template;
            public Mood Expression;
            public bool CanSpeak;
        }
        private readonly ConditionalWeakTable<Dialogue, VoiceBinding> voiceBindings = new ConditionalWeakTable<Dialogue, VoiceBinding>();
        internal ConfigEntry<bool> VoiceEnabled;
        internal ConfigEntry<bool> VoiceWait;
        internal ConfigEntry<float> VoiceVolume;
        private ConfigEntry<string> voiceLanguage;
        internal XiaomanVoicePlayer Voice;
        internal VoiceCatalog Voices = new VoiceCatalog(null);
        private LoadedMod voiceAssets;

        private void InitVoice(ModConfig config)
        {
            VoiceEnabled = config.Define("voice_enabled", true, "小满配音 / Xiaoman Voice", () => Text("cfg_voice_enabled"));
            VoiceVolume = config.Define("voice_volume", 0.9f, "小满配音 / Xiaoman Voice", () => Text("cfg_voice_volume"), min: 0f, max: 1f);
            VoiceWait = config.Define("voice_wait_for_audio", true, "小满配音 / Xiaoman Voice", () => Text("cfg_voice_wait"));
            voiceLanguage = config.Define("voice_language", "日语 / Japanese", "小满配音 / Xiaoman Voice", () => Text("cfg_voice_language"),
                new string[] { "日语 / Japanese", "跟随界面语言 / Follow UI" }, () => Text("cfg_voice_language_tip"));
        }

        private Dialogue BindVoice(Dialogue line, string key, string template = null, Mood? expression = null, bool canSpeak = true)
        {
            VoiceBinding previous;
            Mood pose = expression ?? (voiceBindings.TryGetValue(line, out previous) ? previous.Expression : Mood.Neutral);
            voiceBindings.Remove(line);
            voiceBindings.Add(line, new VoiceBinding { Key = key, Template = template ?? Text(key), Expression = pose, CanSpeak = canSpeak });
            return line;
        }

        internal bool TryLinePerformance(Dialogue line, out Mood expression, out bool canSpeak)
        {
            VoiceBinding binding;
            expression = Mood.Neutral; canSpeak = false;
            if (line == null || !voiceBindings.TryGetValue(line, out binding)) return false;
            expression = binding.Expression; canSpeak = binding.CanSpeak;
            return true;
        }

        private void LoadVoicePack(LoadedMod mod)
        {
            voiceAssets = mod;
            Voices = new VoiceCatalog(null);
            if (!ModHelper.ModFileExists(Id, "Voices/index.json"))
            { Log.Log("No voice index; Xiaoman remains subtitle-only."); return; }
            try
            {
                VoicePackDocument data = JsonConvert.DeserializeObject<VoicePackDocument>(ModHelper.ReadModFileText(Id, "Voices/index.json"));
                if (data == null || data.schema != 1) { Log.Warning("Unsupported voice index; keeping subtitles."); return; }
                Voices = new VoiceCatalog(data.lines);
                if (data.lines != null && Voices.Count != data.lines.Length)
                    Log.Warning("Voice index contains " + data.lines.Length + " entries but only " + Voices.Count + " valid recording bindings.");
                Log.Log("Voice framework ready: " + Voices.Count + " recording(s), " + data.pending_lines + " pending; offline, optional Station Voices bridge.");
                LoadedMod station = ModLoader.Mods.Find(m => m.Manifest.ID == "asteroid_voice_lines" && m.Instance != null);
                Log.Log(station == null ? "Voice mode: standalone (no Station Voices dependency)."
                    : "Station Voices compatibility: v" + station.Manifest.ModVersion + "; host found="
                        + (GameObject.Find("Station Voices") != null) + "; Xiaoman handoff is ordered after asteroid_voice_lines.");
            }
            catch (Exception ex) { Log.Warning("Voice index could not load: " + ex.Message + "; keeping subtitles."); }
        }

        internal string SelectedVoiceLanguage
        {
            get { return voiceLanguage.Value == "日语 / Japanese" ? "ja" : VoiceCatalog.Language(LocHelper.GetCurrentLocaleCode()); }
        }

        internal bool OwnsVoiceLine(Dialogue line)
        {
            if (line == null || line.title == "PLAYER" || line.title == "SEC") return false;
            VoiceBinding binding;
            if (voiceBindings.TryGetValue(line, out binding)) return true;
            return CurrentClientIsXiaoman();
        }

        internal static bool CurrentClientIsXiaoman()
        {
            return PlayerStore.HasInstance && PlayerStore.Instance.currentClientInstance != null
                && PlayerStore.Instance.currentClientInstance.GetClientBlueprint().identifier == ClientId;
        }

        internal VoiceRecording RecordingFor(Dialogue line)
        {
            if (!enabled || !VoiceEnabled.Value || VoiceVolume.Value <= 0 || line == null) return null;
            VoiceBinding binding;
            if (!voiceBindings.TryGetValue(line, out binding) || !binding.CanSpeak) return null;
            VoiceRecording row = Voices.Resolve(binding.Key, SelectedVoiceLanguage, binding.Template);
            AudioClip clip;
            return row != null && voiceAssets != null && voiceAssets.Sounds.TryGetValue(row.sound_key, out clip) && clip != null ? row : null;
        }

        internal AudioClip VoiceClip(VoiceRecording row)
        {
            AudioClip clip;
            return row != null && voiceAssets != null && voiceAssets.Sounds.TryGetValue(row.sound_key, out clip) ? clip : null;
        }

        internal void StopStationVoice(bool owned)
        {
            bool stationLoaded = ModLoader.Mods.Exists(m => m.Manifest.ID == "asteroid_voice_lines" && m.Instance != null);
            if (!VoiceCatalog.SuppressStationVoice(VoiceEnabled.Value, owned, stationLoaded)) return;
            // The installed Station Voices 1.0.6 exposes StopVoice as a Unity message
            // on this host. It also invalidates pending loads, unlike AudioSource.Stop.
            // No dependency, reflection, third-party file edits or global unpatching.
            GameObject host = GameObject.Find("Station Voices");
            if (host != null) host.SendMessage("StopVoice", SendMessageOptions.DontRequireReceiver);
        }
    }

    public sealed class XiaomanVoicePlayer : MonoBehaviour
    {
        private XiaomanMod mod;
        private Dialogue speaking;
        private Dialogue voicedLine;
        private AudioClip clip;
        private SpeechEnvelope mouthEnvelope;
        private readonly Dictionary<AudioClip, SpeechEnvelope> mouthEnvelopes = new Dictionary<AudioClip, SpeechEnvelope>();
        private AudioSource channel;
        private float originalVolume;
        private float appliedVolume;
        private AudioSource typing;
        private bool originalMute;
        private readonly HashSet<Dialogue> pendingActions = new HashSet<Dialogue>();
        private readonly HashSet<Dialogue> pendingEnds = new HashSet<Dialogue>();

        internal void Configure(XiaomanMod owner) { mod = owner; }
        private bool Busy
        { get { return channel != null && clip != null && channel.clip == clip && (AudioListener.pause || channel.isPlaying); } }
        internal bool BusyFor(Dialogue line) { return line != null && speaking == line && Busy; }

        internal bool HasPlayedFor(Dialogue line) { return line != null && voicedLine == line; }

        internal bool PoseFor(Dialogue line, out int mouth)
        {
            mouth = 0;
            if (!BusyFor(line)) return false;
            // Use the audio clock, not the typewriter or wall clock. A paused line
            // keeps its authored expression, with a closed mouth.
            if (!AudioListener.pause && !channel.mute && mod.VoiceVolume.Value > 0)
                mouth = mouthEnvelope == null ? PortraitTiming.Cadence(channel.time) : mouthEnvelope.MouthAt(channel.time);
            return true;
        }

        internal void Display(Dialogue line)
        {
            Stop();
            voicedLine = null;
            if (mod == null || !mod.VoiceEnabled.Value || !mod.OwnsVoiceLine(line)) return;
            mod.StopStationVoice(true);
            VoiceRecording row = mod.RecordingFor(line);
            if (row != null) Play(row, line);
        }

        internal void Single()
        {
            Stop();
            if (mod != null && XiaomanMod.CurrentClientIsXiaoman()) mod.StopStationVoice(true);
        }

        internal void Preview()
        {
            if (!PlayerStore.HasInstance || !AudioManager.Exists || mod == null || !mod.VoiceEnabled.Value) return;
            VoiceRecording row = mod.Voices.Preview("intro_1", mod.SelectedVoiceLanguage);
            if (row == null) return;
            Stop(); mod.StopStationVoice(true); Play(row, null);
        }

        private void Play(VoiceRecording row, Dialogue line)
        {
            AudioClip recording = mod.VoiceClip(row);
            if (recording == null || !AudioManager.Exists) return;
            SpeechEnvelope envelope = EnvelopeFor(recording);
            AudioSource source = AudioManager.Instance.audioSource3;
            if (source == null || !ModHelper.PlaySound3(XiaomanMod.Id, row.sound_key)) return;
            speaking = line; clip = recording; channel = source;
            if (line != null) voicedLine = line;
            mouthEnvelope = envelope;
            originalVolume = source.volume;
            appliedVolume = originalVolume * Mathf.Clamp01(mod.VoiceVolume.Value);
            source.volume = appliedVolume;
            StoreSoundPlayer sound = StoreSoundPlayer.Instance;
            if (sound != null && sound.typewriterAudioSource != null)
            { typing = sound.typewriterAudioSource; originalMute = typing.mute; typing.mute = true; }
            mod.Log.Log("Xiaoman voice: " + row.line_key + " / " + row.language + " / " + recording.length.ToString("0.00") + "s.");
        }

        private SpeechEnvelope EnvelopeFor(AudioClip recording)
        {
            SpeechEnvelope envelope;
            if (mouthEnvelopes.TryGetValue(recording, out envelope)) return envelope;
            envelope = null;
            try
            {
                float[] samples = new float[checked(recording.samples * recording.channels)];
                if (recording.GetData(samples, 0)) envelope = SpeechEnvelope.FromSamples(samples, recording.frequency, recording.channels);
            }
            catch (Exception ex) { mod.Log.Warning("Voice mouth envelope unavailable: " + recording.name + " / " + ex.Message); }
            // If Unity cannot expose decoded PCM, retain audio-clock lip movement.
            mouthEnvelopes[recording] = envelope;
            return envelope;
        }

        internal void Stop()
        {
            if (channel != null)
            {
                if (!Mathf.Approximately(channel.volume, appliedVolume)) originalVolume = channel.volume;
                if (clip != null && channel.clip == clip) channel.Stop();
                channel.volume = originalVolume;
            }
            if (typing != null) typing.mute = originalMute;
            speaking = null; clip = null; channel = null; typing = null; mouthEnvelope = null;
        }

        private void LateUpdate()
        {
            if (clip == null) return;
            if (mod == null || !mod.VoiceEnabled.Value || !Busy
                || (speaking != null && (DialogUIManager.Instance == null || DialogUIManager.Instance.currentDialog != speaking)))
            { Stop(); return; }
            if (channel != null)
            {
                if (!Mathf.Approximately(channel.volume, appliedVolume)) originalVolume = channel.volume;
                appliedVolume = originalVolume * Mathf.Clamp01(mod.VoiceVolume.Value);
                channel.volume = appliedVolume;
            }
            if (typing != null) typing.mute = true;
        }

        internal void WrapFlow(DialogUIManager owner, ref Action action)
        {
            Dialogue line = owner.currentDialog;
            if (mod == null || !mod.VoiceWait.Value || mod.RecordingFor(line) == null) return;
            Action original = action;
            // Use the native mod-owned scheduler so pausing and disabling remain scoped.
            action = () => ModHelper.StartCoroutine(XiaomanMod.Id, WaitFlow(owner, line, original));
        }

        private IEnumerator WaitFlow(DialogUIManager owner, Dialogue line, Action action)
        {
            while (owner != null && owner.currentDialog == line && mod != null && mod.VoiceWait.Value && BusyFor(line)) yield return null;
            if (owner != null && owner.isActiveAndEnabled && owner.currentDialog == line) action();
        }

        internal bool DeferEnd(DialogUIManager owner)
        {
            Dialogue line = owner.currentDialog;
            if (mod == null || !mod.VoiceWait.Value || !BusyFor(line)) return false;
            if (pendingEnds.Add(line)) ModHelper.StartCoroutine(XiaomanMod.Id, WaitEnd(owner, line));
            return true;
        }

        private IEnumerator WaitEnd(DialogUIManager owner, Dialogue line)
        {
            while (owner != null && owner.currentDialog == line && mod != null && mod.VoiceWait.Value && BusyFor(line)) yield return null;
            pendingEnds.Remove(line);
            if (owner != null && owner.isActiveAndEnabled && owner.currentDialog == line) owner.EndConversation();
        }

        internal bool DeferAction(Dialogue line)
        {
            if (mod == null || !mod.VoiceWait.Value || line.endAction == null || line.isEndActionExecuted || !BusyFor(line)) return false;
            if (pendingActions.Add(line)) ModHelper.StartCoroutine(XiaomanMod.Id, WaitAction(line));
            return true;
        }

        private IEnumerator WaitAction(Dialogue line)
        {
            while (mod != null && mod.VoiceWait.Value && BusyFor(line)) yield return null;
            pendingActions.Remove(line);
            if (!line.isEndActionExecuted) line.ExecuteEndAction();
        }

        internal void Shutdown()
        {
            Stop();
            foreach (Dialogue line in new List<Dialogue>(pendingActions))
                if (!line.isEndActionExecuted) line.ExecuteEndAction();
            ModHelper.StopAllCoroutines(XiaomanMod.Id); pendingActions.Clear(); pendingEnds.Clear();
        }
    }
}
