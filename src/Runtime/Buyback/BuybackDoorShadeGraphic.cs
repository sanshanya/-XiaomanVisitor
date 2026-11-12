using UnityEngine;
using UnityEngine.UI;

namespace XiaomanVisitor.SideStories
{
    // A moving soft edge, attached to the CG. The original artwork is unchanged.
    internal sealed class BuybackDoorShadeGraphic : MaskableGraphic
    {
        private float progress;
        internal float Progress
        {
            set { progress = Mathf.Clamp01(value); SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper mesh) { Build(mesh, GetPixelAdjustedRect(), progress); }

        internal static void Build(VertexHelper mesh, Rect area, float progress)
        {
            mesh.Clear();
            if (progress >= 1) return;
            float feather = Mathf.Clamp(area.width * .07f, 16, 64);
            float edge = area.xMin - feather + Mathf.Clamp01(progress) * (area.width + feather);
            float left = Mathf.Max(area.xMin, edge), right = Mathf.Min(area.xMax, edge + feather);
            if (right > left)
                Quad(mesh, area, left, right, (byte)(255 * (left - edge) / feather), (byte)(255 * (right - edge) / feather));
            left = Mathf.Max(area.xMin, edge + feather);
            if (left < area.xMax) Quad(mesh, area, left, area.xMax, 255, 255);
        }

        private static void Quad(VertexHelper mesh, Rect area, float left, float right, byte alphaLeft, byte alphaRight)
        {
            int first = mesh.currentVertCount;
            var a = new Color32(0, 0, 0, alphaLeft); var b = new Color32(0, 0, 0, alphaRight);
            mesh.AddVert(new Vector3(left, area.yMin), a, Vector2.zero);
            mesh.AddVert(new Vector3(right, area.yMin), b, Vector2.zero);
            mesh.AddVert(new Vector3(right, area.yMax), b, Vector2.zero);
            mesh.AddVert(new Vector3(left, area.yMax), a, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2); mesh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
