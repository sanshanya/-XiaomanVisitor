using System.Linq;
using HarmonyLib;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        internal string ThirdNoteVariant
        { get { return PlayerStore.HasInstance ? ReadStory().Visits.FirstOrDefault(v => v.Index==3)?.NoteVariant ?? "A" : "A"; } }
        private void FreezeThirdWorkLetter()
        {
            if (!PlayerStore.HasInstance) return;
            var story=ReadStory();
            var work=ReadWork();
            if (story.FreezeThirdNote(StoreStation.Instance.dayCounter,work.WorkedOn)) SaveStory(story);
        }
    }
    [HarmonyPatch(typeof(PaperUI),nameof(PaperUI.ResolveText),new[]{typeof(PaperElementData),typeof(PaperPageData)})]
    public static class XiaomanWorkLetterTextPatch
    {
        private static void Postfix(PaperUI __instance,PaperElementData element,ref string __result)
        {
            var mod=XiaomanMod.Instance;
            if (mod==null || __instance.EditorMode || __instance.ModId!=XiaomanMod.Id || __instance.Data?.id!="story_note_03"
                || mod.ThirdNoteVariant!="B" || element==null || !new[]{"title","body","signature"}.Contains(element.id)) return;
            __result=ModHelper.GetLocalized(XiaomanMod.Id,"story_note_03_b_"+element.id);
        }
    }
}
