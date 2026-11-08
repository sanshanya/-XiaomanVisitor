using System;

namespace XiaomanVisitor
{
    public struct DialoguePose
    {
        public readonly Mood Expression;
        public readonly int Mouth;
        public readonly bool Speaking;

        public DialoguePose(Mood expression, int mouth, bool speaking)
        { Expression = expression; Mouth = mouth; Speaking = speaking; }
    }

    public static class PortraitTiming
    {
        // A recorded line stays authoritative after it stops: unfinished subtitles
        // must not restart its mouth after a manual skip or a channel interruption.
        public static DialoguePose Resolve(bool visible, bool canSpeak, Mood expression,
            bool hadVoice, bool voiceBusy, bool typing, int voiceMouth, float textClock)
        {
            bool speaking = visible && canSpeak && (hadVoice ? voiceBusy : typing);
            return speaking ? new DialoguePose(expression, hadVoice ? Math.Max(0, Math.Min(2, voiceMouth)) : Cadence(textClock), true)
                : new DialoguePose(Mood.Neutral, 0, false);
        }

        public static int Cadence(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0) return 0;
            int step = (int)((seconds % 0.48f) / 0.12f);
            return step == 3 ? 1 : step;
        }

        public static bool MayBlink(Mood expression)
        { return expression == Mood.Neutral || expression == Mood.Soft; }
    }

    // Small, clip-local amplitude envelope; it never changes the approved recording.
    // Cache one 40ms level per window rather than retaining another copy of the PCM.
    public sealed class SpeechEnvelope
    {
        private readonly float[] levels;
        private readonly float stepSeconds, duration, quiet, wide;

        private SpeechEnvelope(float[] values, float step, float length, float peak)
        { levels = values; stepSeconds = step; duration = length; quiet = Math.Max(0.0035f, peak * 0.08f); wide = peak * 0.32f; }

        public static SpeechEnvelope FromSamples(float[] samples, int sampleRate, int channels)
        {
            if (samples == null || sampleRate <= 0 || channels <= 0 || samples.Length % channels != 0)
                throw new ArgumentException("Invalid voice PCM format.");
            int frames = samples.Length / channels;
            int windowFrames = Math.Max(1, (int)Math.Round(sampleRate * 0.04));
            float[] values = new float[(frames + windowFrames - 1) / windowFrames];
            float peak = 0;
            for (int window = 0; window < values.Length; window++)
            {
                int start = window * windowFrames * channels;
                int end = Math.Min(samples.Length, start + windowFrames * channels);
                double energy = 0;
                for (int i = start; i < end; i++) energy += (double)samples[i] * samples[i];
                values[window] = (float)Math.Sqrt(energy / (end - start));
                peak = Math.Max(peak, values[window]);
            }
            return new SpeechEnvelope(values, (float)windowFrames / sampleRate, (float)frames / sampleRate, peak);
        }

        public int MouthAt(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0 || seconds >= duration || levels.Length == 0) return 0;
            int index = Math.Min(levels.Length - 1, (int)(seconds / stepSeconds));
            float level = (levels[Math.Max(0, index - 1)] + levels[index] * 2 + levels[Math.Min(levels.Length - 1, index + 1)]) * 0.25f;
            if (level <= quiet) return 0;
            int cadence = PortraitTiming.Cadence(seconds);
            return level < wide ? Math.Min(1, cadence) : cadence;
        }
    }
}
