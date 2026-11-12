using System;
using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        internal float BuybackVoiceGain { get { return VoiceEnabled != null && VoiceEnabled.Value
            ? Mathf.Clamp01(VoiceVolume.Value) : 0; } }
        internal AudioClip BuybackRecording(string key, string subtitle)
        {
            if (BuybackVoiceGain <= 0 || string.IsNullOrEmpty(key)) return null;
            // Same reviewed index, language preference and exact subtitle binding
            // as the visitor. A missing recording leaves the authored subtitles.
            return VoiceClip(Voices.Resolve(key, SelectedVoiceLanguage, subtitle));
        }
    }
}

namespace XiaomanVisitor.SideStories
{
    internal sealed partial class BuybackStory
    {
        internal float StageVoiceGain { get { return visitor?.BuybackVoiceGain ?? 0; } }
        internal AudioClip StageRecording(string key, string subtitle)
        { return visitor?.BuybackRecording(key, subtitle); }

        internal string PreludeVoiceKey(string subtitle) { return PreludeVoiceKey(PreludeText(), subtitle); }
        internal static string PreludeVoiceKey(BuybackPreludeText text, string subtitle)
        {
            // Return prefixes differ on Monday, delayed visits and weekdays.
            // Match only this branch's explicitly authored, exact subtitles.
            var fields = new[] { "first_accept", "first_return", "second_accept", "second_return" };
            var groups = new[] { text.first_accept, text.first_return, text.second_accept, text.second_return };
            for (int g = 0; g < groups.Length; g++)
                for (int i = 0; i < groups[g].Length; i++)
                    if (string.Equals(groups[g][i], subtitle, StringComparison.Ordinal))
                        return "buyback_prelude_" + fields[g] + "_" + i;
            fields = new[] { "late_return", "repeat", "accept_clean", "goodbye" };
            var singles = new[] { text.late_return, text.repeat, text.accept_clean, text.goodbye };
            for (int i = 0; i < singles.Length; i++)
                if (string.Equals(singles[i], subtitle, StringComparison.Ordinal)) return "buyback_prelude_" + fields[i];
            return null;
        }
    }

    internal sealed partial class BuybackStage
    {
        private AudioSource speechAudio;
        private bool recordedLine;

        private void StartVoice(string key, string text)
        {
            StopVoice();
            var owner = BuybackStory.Instance;
            var clip = owner?.StageRecording(key, text);
            if (clip == null) return;
            if (speechAudio == null) {
                speechAudio = gameObject.AddComponent<AudioSource>();
                speechAudio.playOnAwake = false; speechAudio.loop = false; speechAudio.spatialBlend = 0;
                speechAudio.outputAudioMixerGroup = StoreSoundPlayer.Instance?.soundAudioSource?.outputAudioMixerGroup;
            }
            speechAudio.clip = clip;
            speechAudio.volume = owner.StageVoiceGain;
            speechAudio.Play(); recordedLine = true;
        }
        private void StopVoice()
        {
            recordedLine = false;
            if (speechAudio == null) return;
            speechAudio.Stop(); speechAudio.clip = null;
        }
        private void UpdateVoice()
        {
            if (speechAudio == null || !recordedLine) return;
            speechAudio.volume = BuybackStory.Instance?.StageVoiceGain ?? 0;
            if (speechAudio.volume <= 0) StopVoice();
        }
    }
}
