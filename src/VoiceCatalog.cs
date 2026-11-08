using System;
using System.Collections.Generic;
using System.Linq;

namespace XiaomanVisitor
{
    [Serializable]
    public sealed class VoicePackDocument
    {
        public int schema;
        public VoiceRecording[] lines;
        public int pending_lines;
    }

    [Serializable]
    public sealed class VoiceRecording
    {
        public string line_key;
        public string language;
        public string sound_key;
        public string spoken_text;
        public string[] subtitle_templates;
        public bool generic;
    }

    public sealed class VoiceCatalog
    {
        private readonly Dictionary<string, VoiceRecording> recordings = new Dictionary<string, VoiceRecording>(StringComparer.Ordinal);
        public int Count { get { return recordings.Count; } }

        public VoiceCatalog(IEnumerable<VoiceRecording> rows)
        {
            foreach (VoiceRecording row in rows ?? Enumerable.Empty<VoiceRecording>())
            {
                if (row == null || string.IsNullOrEmpty(row.line_key) || !SafeSoundKey(row.sound_key)
                    || string.IsNullOrWhiteSpace(row.spoken_text) || row.spoken_text.Contains("{")
                    || row.spoken_text.Contains("}") || row.subtitle_templates == null) continue;
                string language = Language(row.language);
                if (language != "ja" && language != "zh" && language != "en") continue;
                recordings[row.line_key + "\n" + language] = row;
            }
        }

        public VoiceRecording Resolve(string key, string language, string template)
        {
            VoiceRecording row;
            if (key == null || template == null || !recordings.TryGetValue(key + "\n" + Language(language), out row)) return null;
            if (!row.subtitle_templates.Contains(template, StringComparer.Ordinal)) return null;
            if ((template.Contains("{") || template.Contains("}")) && !row.generic) return null;
            return row;
        }

        public VoiceRecording Preview(string key, string language)
        {
            VoiceRecording row;
            return recordings.TryGetValue(key + "\n" + Language(language), out row) ? row : null;
        }

        public static string Language(string value)
        { return (value ?? "").Trim().ToLowerInvariant().Split('-', '_')[0]; }

        public static bool SafeSoundKey(string value)
        {
            return !string.IsNullOrEmpty(value) && value.All(c => c == '_' || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'));
        }

        public static bool SuppressStationVoice(bool enabled, bool ownedLine, bool stationLoaded)
        { return enabled && ownedLine && stationLoaded; }
    }
}
