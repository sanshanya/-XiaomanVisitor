using System;
using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        // One owner and one persistent high-detail texture for both scenes.
        // The nearest atlas is tried only if the accepted shared asset fails.
        private Texture2D sharedPortraitAtlas;
        private bool portraitLoadAttempted;

        internal Texture2D SharedPortraitAtlas(out bool detailed)
        {
            if (!portraitLoadAttempted && SystemInfo.maxTextureSize >= 2304) {
                portraitLoadAttempted = true;
                foreach (string file in new[] { PortraitDetailRules.PreferredFile, PortraitDetailRules.HighFile }) {
                    Texture2D pending = null;
                    try {
                        byte[] data = ModHelper.ReadModFileBytes(Id,file);
                        if (data == null || data.Length == 0) throw new InvalidOperationException("Missing portrait atlas");
                        pending = new Texture2D(2,2,TextureFormat.RGBA32,false);
                        if (!ImageConversion.LoadImage(pending,data,true) || pending.width != 2304 || pending.height != 2064)
                            throw new InvalidOperationException("Invalid portrait atlas dimensions");
                        pending.filterMode = FilterMode.Point;
                        pending.wrapMode = TextureWrapMode.Clamp;
                        pending.anisoLevel = 0;
                        pending.name = "Xiaoman.Portraits.Shared.Point.NoMipmaps";
                        sharedPortraitAtlas = pending; pending = null;
                        break;
                    }
                    catch (Exception error) { Log.Warning("Portrait fallback (" + file + "): " + error.Message); }
                    finally { if (pending != null) UnityEngine.Object.Destroy(pending); }
                }
            }
            detailed = sharedPortraitAtlas != null;
            return sharedPortraitAtlas ?? atlas;
        }

        private void ReleasePortraitAssets()
        {
            if (sharedPortraitAtlas != null) UnityEngine.Object.Destroy(sharedPortraitAtlas);
            sharedPortraitAtlas = null; portraitLoadAttempted = false;
        }
    }
}
