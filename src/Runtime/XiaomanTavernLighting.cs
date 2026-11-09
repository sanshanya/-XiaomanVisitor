using System;
using UnityEngine;
using UnityEngine.UI;

namespace XiaomanVisitor
{
    // Native UI shader with a single owned material for >1 RGB headroom.
    // Keep the shared atlas, alpha and frame UVs; no overlays or GPU readbacks.
    [DefaultExecutionOrder(1100)]
    internal sealed class XiaomanTavernLighting : BaseMeshEffect
    {
        private TavernActorLight light;
        private XiaomanTavernAmbient ambient;
        private Material originalMaterial, lightMaterial;
        private int currentFrame;

        internal void Configure(XiaomanMod mod, XiaomanTavernAmbient source)
        {
            ambient = source;
            try {
                light = TavernActorLight.Parse(ModHelper.ReadModFileText(XiaomanMod.Id, TavernRules.SceneFolder + "actor-light.json"));
                originalMaterial = graphic.material;
                if (!originalMaterial.HasProperty("_Color")) throw new InvalidOperationException("UI tint is unavailable");
                lightMaterial = new Material(originalMaterial) { name = "Xiaoman Tavern Light" };
                lightMaterial.SetVector("_Color", new Vector4(light.GainScale, light.GainScale, light.GainScale, 1f));
                graphic.material = lightMaterial;
                graphic.SetVerticesDirty();
            }
            catch (Exception error) {
                light = null; ReleaseMaterial();
                mod.Log.Warning("Tavern actor uses original colors: " + error.Message);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (light == null || graphic == null) return;
            int frame = light.FrameAt(ambient == null ? -1 : ambient.DisplayedFrame);
            if (frame == currentFrame) return;
            currentFrame = frame;
            graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || light == null || mesh.currentVertCount != 4) return;
            UIVertex bl = default(UIVertex), tl = bl, tr = bl, br = bl;
            mesh.PopulateUIVertex(ref bl, 0); mesh.PopulateUIVertex(ref tl, 1);
            mesh.PopulateUIVertex(ref tr, 2); mesh.PopulateUIVertex(ref br, 3);
            mesh.Clear();
            int columns = light.X.Length;
            for (int row = 0; row < light.Y.Length; row++) {
                float v = light.Y[row];
                for (int col = 0; col < columns; col++) {
                    float u = light.X[col];
                    UIVertex vertex = bl;
                    vertex.position = Vector3.Lerp(Vector3.Lerp(bl.position, br.position, u), Vector3.Lerp(tl.position, tr.position, u), v);
                    vertex.uv0 = Vector4.Lerp(Vector4.Lerp(bl.uv0, br.uv0, u), Vector4.Lerp(tl.uv0, tr.uv0, u), v);
                    Color32 original = Color32.Lerp(Color32.Lerp(bl.color, br.color, u), Color32.Lerp(tl.color, tr.color, u), v);
                    int offset = (currentFrame * light.Nodes + row * columns + col) * 3;
                    vertex.color = new Color32((byte)((original.r * light.RGB[offset] + 127) / 255),
                        (byte)((original.g * light.RGB[offset+1] + 127) / 255),
                        (byte)((original.b * light.RGB[offset+2] + 127) / 255), original.a);
                    mesh.AddVert(vertex);
                    if (row > 0 && col > 0) {
                        int a = (row - 1) * columns + col - 1, b = a + columns;
                        mesh.AddTriangle(a, b, b+1); mesh.AddTriangle(b+1, a+1, a);
                    }
                }
            }
        }

        protected override void OnEnable()
        { base.OnEnable(); if (lightMaterial != null && graphic != null) graphic.material = lightMaterial; }

        protected override void OnDisable()
        { currentFrame = 0; RestoreMaterial(); base.OnDisable(); }

        private void RestoreMaterial()
        { if (graphic != null && graphic.material == lightMaterial) graphic.material = originalMaterial; }

        private void ReleaseMaterial()
        {
            RestoreMaterial();
            if (lightMaterial != null) UnityEngine.Object.Destroy(lightMaterial);
            lightMaterial = null;
        }

        protected override void OnDestroy()
        { ReleaseMaterial(); base.OnDestroy(); }
    }
}
