using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private Texture2D activePortraitAtlas;
        internal bool HasDetailedPortraits { get { return activePortraitAtlas != null && activePortraitAtlas != atlas; } }
        internal bool OwnsPortrait(Sprite sprite)
        {
            return sprite != null && ((activePortraitAtlas != null && sprite.texture == activePortraitAtlas)
                || (gunAtlas != null && sprite.texture == gunAtlas));
        }

        private Texture2D SelectPortraitAtlas()
        {
            if (!PortraitDetailRules.WantsHighDetail(LetterboxManager.GameAreaHeight, SystemInfo.maxTextureSize)) return atlas;
            bool detailed;
            return SharedPortraitAtlas(out detailed);
        }
    }
}
