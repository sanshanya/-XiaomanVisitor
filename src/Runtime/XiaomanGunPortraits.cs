using System;
using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private const string GunPortraitFile = "Portraits/xiaoman_gun_atlas.png";
        private readonly Sprite[,] gunFrames = new Sprite[2, 3];
        private Texture2D gunAtlas;
        private bool gunAtlasAttempted;

        private bool EnsureGunPortraits()
        {
            Sprite reference = frames[0, 0];
            if (reference == null) return false;
            if (!gunAtlasAttempted) {
                gunAtlasAttempted = true;
                Texture2D pending = null;
                try {
                    byte[] bytes = ModHelper.ReadModFileBytes(Id, GunPortraitFile);
                    pending = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (bytes == null || !ImageConversion.LoadImage(pending, bytes, true)
                        || pending.width != 1152 || pending.height != 1376)
                        throw new InvalidOperationException("Invalid gun reaction portrait atlas");
                    pending.filterMode = FilterMode.Point; pending.wrapMode = TextureWrapMode.Clamp; pending.anisoLevel = 0;
                    pending.name = "Xiaoman.Portraits.Gun.Point.NoMipmaps";
                    gunAtlas = pending; pending = null;
                }
                catch (Exception error) { Log.Warning("Gun reaction portrait fallback: " + error.Message); }
                finally { if (pending != null) UnityEngine.Object.Destroy(pending); }
            }
            if (gunAtlas == null) return false;
            if (gunFrames[0, 0] != null) return true;
            Vector2 pivot = new Vector2(reference.pivot.x / reference.rect.width, reference.pivot.y / reference.rect.height);
            float ppu = reference.pixelsPerUnit * (384f / reference.rect.width);
            for (int row = 0; row < 2; row++) for (int col = 0; col < 3; col++) {
                Sprite sprite = Sprite.Create(gunAtlas, new Rect(col * 384, (1 - row) * 688, 384, 688), pivot, ppu, 0, SpriteMeshType.FullRect);
                sprite.name = "Xiaoman_Gun_" + row + "_" + col;
                gunFrames[row, col] = sprite;
            }
            return true;
        }

        private void DestroyGunSprites()
        {
            for (int row = 0; row < 2; row++) for (int col = 0; col < 3; col++) {
                if (gunFrames[row, col] != null) UnityEngine.Object.Destroy(gunFrames[row, col]);
                gunFrames[row, col] = null;
            }
        }

        private void ReleaseGunPortraits()
        {
            DestroyGunSprites();
            if (gunAtlas != null) UnityEngine.Object.Destroy(gunAtlas);
            gunAtlas = null; gunAtlasAttempted = false;
        }
    }
}
