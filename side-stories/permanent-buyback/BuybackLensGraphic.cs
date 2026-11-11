using System;
using UnityEngine;
using UnityEngine.UI;

namespace XiaomanVisitor.SideStories
{
    // Screen-space UI geometry: no new texture, camera effect, game RNG or CG edit.
    internal sealed class BuybackLensGraphic : MaskableGraphic
    {
        internal int Style; // 0: spray, 1: slow drips, 2: peripheral darkness.
        private int beats;
        internal int Beats { set { beats = value; SetVerticesDirty(); } }
        protected override void OnPopulateMesh(VertexHelper mesh) { BuildBeat(mesh, rectTransform.rect, Style, beats); }

        internal static void Build(VertexHelper mesh, Rect area, int style)
        { BuildBeat(mesh, area, style, 3); }
        internal static void BuildBeat(VertexHelper mesh, Rect area, int style, int beats)
        {
            mesh.Clear();
            if (beats <= 0) return;
            if (style == 2) { Vignette(mesh, area); return; }
            if (style == 1)
            {
                Drop(mesh, area, .20f, .61f, .005f, 7.0f, 13);
                if (beats >= 2) Drop(mesh, area, .79f, .53f, .006f, 8.0f, 19);
                if (beats >= 3) Drop(mesh, area, .29f, .30f, .004f, 5.0f, 29);
                return;
            }
            // Main stains stay away from the face; subtitles draw above this layer.
            Drop(mesh, area, .16f, .66f, .052f, 1.25f, 7);
            if (beats >= 2) Drop(mesh, area, .83f, .58f, .043f, 1.15f, 17);
            if (beats >= 3)
            {
                Drop(mesh, area, .23f, .48f, .032f, .85f, 11);
                Drop(mesh, area, .75f, .29f, .031f, 1.30f, 23);
                Drop(mesh, area, .08f, .22f, .042f, .85f, 31);
            }
            Streak(mesh, area, .16f, .66f, .14f, .81f, .003f);
            Streak(mesh, area, .16f, .66f, .27f, .74f, .002f);
            Streak(mesh, area, .16f, .66f, .30f, .56f, .0025f);
            Streak(mesh, area, .16f, .66f, .08f, .55f, .002f);
            if (beats >= 2)
            {
                Streak(mesh, area, .83f, .58f, .72f, .70f, .002f);
                Streak(mesh, area, .83f, .58f, .78f, .82f, .0025f);
                Streak(mesh, area, .83f, .58f, .94f, .68f, .003f);
                Streak(mesh, area, .83f, .58f, .93f, .49f, .002f);
                Streak(mesh, area, .83f, .58f, .71f, .45f, .002f);
            }
            for (int i = 0; i < 60; i++)
            {
                if (i % 3 >= beats) continue;
                float u = Noise(i * 3 + 4), v = Noise(i * 3 + 5);
                bool left = i % 2 == 0;
                float x = left ? .035f + .30f * u : .665f + .30f * u;
                float y = .14f + .74f * v;
                float radius = .0018f + .008f * Noise(i * 3 + 6);
                Drop(mesh, area, x, y, radius, .8f + Noise(i + 91), i + 41);
            }
        }

        private static float Noise(int seed)
        {
            double value = Math.Sin(seed * 127.1 + 311.7) * 43758.5453;
            return (float)(value - Math.Floor(value));
        }

        private static void Drop(VertexHelper mesh, Rect area, float x, float y, float radius, float stretch, int seed)
        {
            const int sides = 18;
            Vector2 center = new Vector2(area.xMin + area.width * x, area.yMin + area.height * y);
            int first = mesh.currentVertCount;
            var ink = new Color32(104, 7, 24, (byte)(155 + Noise(seed + 40) * 65));
            mesh.AddVert(center, ink, Vector2.zero);
            for (int ring = 0; ring < 2; ring++)
            {
                for (int i = 0; i < sides; i++)
                {
                    double angle = i * Math.PI * 2 / sides;
                    float uneven = .78f + .38f * Noise(seed * 19 + i);
                    float r = radius * area.height * uneven * (ring == 0 ? .78f : 1.0f);
                    Vector2 point = center + new Vector2((float)Math.Cos(angle) * r,
                        (float)Math.Sin(angle) * r * stretch);
                    mesh.AddVert(point, ring == 0 ? ink : new Color32(78, 0, 12, 0), Vector2.zero);
                }
            }
            for (int i = 0; i < sides; i++)
            {
                int a = first + 1 + i, b = first + 1 + (i + 1) % sides;
                mesh.AddTriangle(first, a, b);
                mesh.AddTriangle(a, a + sides, b);
                mesh.AddTriangle(b, a + sides, b + sides);
            }
        }

        private static void Streak(VertexHelper mesh, Rect area, float x1, float y1, float x2, float y2, float thickness)
        {
            var start = new Vector2(area.xMin + x1 * area.width, area.yMin + y1 * area.height);
            var end = new Vector2(area.xMin + x2 * area.width, area.yMin + y2 * area.height);
            var delta = end - start;
            float length = (float)Math.Sqrt(delta.x * delta.x + delta.y * delta.y);
            var normal = new Vector2(-delta.y, delta.x) / length * (area.height * thickness);
            int first = mesh.currentVertCount;
            var clear = new Color32(80, 0, 15, 0);
            var ink = new Color32(108, 5, 20, 175);
            mesh.AddVert(start - normal, clear, Vector2.zero);
            mesh.AddVert(start, ink, Vector2.zero);
            mesh.AddVert(start + normal, clear, Vector2.zero);
            mesh.AddVert(end - normal * .12f, clear, Vector2.zero);
            mesh.AddVert(end, new Color32(95, 3, 18, 80), Vector2.zero);
            mesh.AddVert(end + normal * .12f, clear, Vector2.zero);
            mesh.AddTriangle(first, first + 3, first + 1); mesh.AddTriangle(first + 1, first + 3, first + 4);
            mesh.AddTriangle(first + 1, first + 4, first + 2); mesh.AddTriangle(first + 2, first + 4, first + 5);
        }

        private static void Vignette(VertexHelper mesh, Rect area)
        {
            const int sides = 64;
            Vector2 center = area.center;
            for (int i = 0; i < sides; i++)
            {
                double angle = i * Math.PI * 2 / sides;
                float x = (float)Math.Cos(angle), y = (float)Math.Sin(angle);
                mesh.AddVert(center + new Vector2(x * area.width * .34f, y * area.height * .39f),
                    new Color32(12, 2, 9, 0), Vector2.zero);
                float edge = .72f / Math.Max(Math.Abs(x), Math.Abs(y));
                mesh.AddVert(center + new Vector2(x * area.width * edge, y * area.height * edge),
                    new Color32(8, 0, 5, 205), Vector2.zero);
            }
            for (int i = 0; i < sides; i++)
            {
                int a = i * 2, b = ((i + 1) % sides) * 2;
                mesh.AddTriangle(a, a + 1, b); mesh.AddTriangle(b, a + 1, b + 1);
            }
        }
    }
}
