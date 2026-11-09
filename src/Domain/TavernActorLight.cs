using System;
using Newtonsoft.Json;

namespace XiaomanVisitor
{
    public sealed class TavernActorLight
    {
        public readonly float[] X, Y;
        public readonly byte[] RGB;
        public readonly int Frames;
        public readonly float Fps, GainScale;
        public int Nodes { get { return X.Length * Y.Length; } }

        private TavernActorLight(TavernActorLightDocument data, byte[] rgb)
        { X = data.x; Y = data.y; Frames = data.frame_count; Fps = data.fps; GainScale = data.gain_scale; RGB = rgb; }

        public static TavernActorLight Parse(string json)
        {
            var data = JsonConvert.DeserializeObject<TavernActorLightDocument>(json ?? "");
            if (data == null || data.schema != 2 || data.frame_count < 1 || data.frame_count > 600
                || float.IsNaN(data.fps) || float.IsInfinity(data.fps) || data.fps <= 0
                || float.IsNaN(data.gain_scale) || float.IsInfinity(data.gain_scale) || data.gain_scale < 1 || data.gain_scale > 2
                || !ValidNodes(data.x) || !ValidNodes(data.y)) throw new FormatException("Invalid actor light table");
            byte[] rgb = Convert.FromBase64String(data.rgb ?? "");
            if (rgb.Length != checked(data.frame_count * data.x.Length * data.y.Length * 3))
                throw new FormatException("Incomplete actor light table");
            return new TavernActorLight(data, rgb);
        }

        public int FrameAt(long decodedFrame)
        { return decodedFrame < 0 ? 0 : (int)(decodedFrame % Frames); }

        private static bool ValidNodes(float[] nodes)
        {
            if (nodes == null || nodes.Length < 2 || nodes.Length > 16 || nodes[0] != 0 || nodes[nodes.Length - 1] != 1) return false;
            for (int i = 1; i < nodes.Length; i++)
                if (float.IsNaN(nodes[i]) || float.IsInfinity(nodes[i]) || nodes[i] <= nodes[i-1] || nodes[i] > 1) return false;
            return true;
        }
    }

    public sealed class TavernActorLightDocument
    {
        public int schema, frame_count;
        public float fps, gain_scale;
        public float[] x, y;
        public string rgb;
    }
}
