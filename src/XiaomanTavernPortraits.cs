using System;
using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        // Night UI owns its texture. It never changes the store atlas selection,
        // lazy-load flags, sprite registry or display settings from 0.0.2.
        private Texture2D tavernHighAtlas;
        private bool tavernHighAttempted;
        internal const string TavernAtlasFile = "Portraits/xiaoman_tavern_atlas.png";

        internal Texture2D TavernPortraitAtlas(out bool highDetail)
        {
            if (!tavernHighAttempted && SystemInfo.maxTextureSize >= 2304) {
                tavernHighAttempted = true;
                Texture2D pending = null;
                try {
                    byte[] bytes = ModHelper.ReadModFileBytes(Id, TavernAtlasFile);
                    if (bytes == null) throw new InvalidOperationException("Missing night portrait atlas");
                    pending = new Texture2D(2,2,TextureFormat.RGBA32,false);
                    if (!ImageConversion.LoadImage(pending,bytes,true) || pending.width != 2304 || pending.height != 2064)
                        throw new InvalidOperationException("Invalid night portrait atlas");
                    pending.filterMode = FilterMode.Point; pending.wrapMode = TextureWrapMode.Clamp; pending.anisoLevel = 0;
                    pending.name = "Xiaoman.Tavern.IndependentAtlas";
                    tavernHighAtlas = pending; pending = null;
                }
                catch (Exception error) { Log.Warning("Night portraits use fallback: " + error.Message); }
                finally { if (pending != null) UnityEngine.Object.Destroy(pending); }
            }
            highDetail = tavernHighAtlas != null;
            return tavernHighAtlas ?? atlas;
        }

        private void ReleaseTavernPortraits()
        {
            if (tavernHighAtlas != null) UnityEngine.Object.Destroy(tavernHighAtlas);
            tavernHighAtlas = null; tavernHighAttempted = false;
        }
    }
}
