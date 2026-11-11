using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private GameItem tavernDeck;
        private readonly HashSet<GameInventory> tavernTapeInventories=new HashSet<GameInventory>();
        private void OpenTavernCassettePlayer()
        {
            if (!TavernMayAct || TavernDialogueBusy) return;
            CloseTavernMenu();
            if (tavernDeck==null) {
                tavernDeck=MachineCassettePlayer.CassettePlayer();tavernDeck.identifier=Id+".tavern.deck";ModHelper.SetItemOwnerShip(tavernDeck,true);
                tavernDeck.contentWindow.SetTitle(Text("ui_w04_deck"));
                var storage=(GameInventory)MachineHelper.GetBodyStorage(tavernDeck,0);
                var bay=(GameInventory)MachineHelper.GetBodyStorage(tavernDeck,1);
                tavernTapeInventories.Add(storage);tavernTapeInventories.Add(bay);
                var box=CreateCassetteCase();var boxInv=CassetteCaseInventory(box);tavernTapeInventories.Add(boxInv);
                box.mayInventoryAddItemFunc=(i,dest) => dest==storage;
                storage.mayInventoryAddItemFunc=(i,_) => i==box || i.IsTag("XIAOMAN_TAVERN_TAPE");
                bay.mayInventoryAddItemFunc=(i,_) => i.IsTag("XIAOMAN_TAVERN_TAPE");
                boxInv.mayInventoryAddItemFunc=(i,_) => i.IsTag("XIAOMAN_TAVERN_TAPE");
                ModHelper.SetItemOwnerShip(box,true);GraphUtils.TryAcceptAll(storage,box);
                // Show the one current-chapter tape directly in native storage.
                // The case remains optional; reopening never creates another tape.
                var track=CassetteCollection.Find(TavernRules.MusicKey(ReadStory().VisitIndex));
                var tape=CreateCollectionCassette(track);ModHelper.SetItemOwnerShip(tape,true);
                tape.EnableTag("XIAOMAN_TAVERN_TAPE");tape.mayInventoryAddItemFunc=(i,dest) => tavernTapeInventories.Contains(dest);
                GraphUtils.TryAcceptAll(storage,tape);
                if (tape.parentInventory==null) { tape.Destroy();throw new InvalidOperationException("Tavern tape storage is full"); }
            }
            tavernDeck.contentWindow.SetTitle(Text("ui_w04_deck"));tavernDeck.contentWindow.Show();
        }
        private void CloseTavernCassettePlayer()
        {
            if (tavernDeck!=null) { tavernDeck.Destroy();tavernDeck=null; }
            tavernTapeInventories.Clear();
        }
        internal bool OwnsTavernDeck(GameItem machine) { return tavernDeck!=null && ReferenceEquals(machine,tavernDeck); }
        internal bool TavernDeckCommand(GameItem machine,string command,GameGridInventory bay=null)
        {
            if (!OwnsTavernDeck(machine)) return false;
            if (!TavernMayAct) return true;
            var tracks=bay==null?null:MachineCassettePlayer.GetTrackListFromCassette(bay);
            int current=machine.GetTagReadonly(MachineCassettePlayer.CURRENT_PLAYING_CASSETTE_INDEX_TAG).GetInt();
            int state=machine.GetTagReadonly(MachineCassettePlayer.CASETTE_PLAYER_STATE_INT).GetInt();
            if (command=="changed" && (tracks==null || tracks.Count==0)) { tavernStage.PauseMusic();machine.ModifyTag(MachineCassettePlayer.CASETTE_PLAYER_STATE_INT,t => t.Enable().SetInt(0));return true; }
            if (command=="changed") { if (state==0) return true;command="play";state=0; }
            if (command=="stop") { tavernStage.PauseMusic();state=0; }
            else if (command=="play" && state==1) { tavernStage.PauseMusic();state=2; }
            else if (command=="play" && state==2) { tavernStage.ResumeMusic();state=1; }
            else if (tracks!=null && tracks.Count>0) {
                current=(current+(command=="next"?1:command=="previous"?-1:0)+tracks.Count)%tracks.Count;
                var track=CassetteCollection.Tracks.FirstOrDefault(t => ModHelper.GetSoundIdentifier(Id,t.SoundKey)==tracks[current]);
                if (track!=null && tavernStage.PlayMusic(track.Key)) { UpdateTavern(p => p.SelectedTrack=track.Key);state=1; }
            }
            machine.ModifyTag(MachineCassettePlayer.CURRENT_PLAYING_CASSETTE_INDEX_TAG,t => t.Enable().SetInt(current));
            machine.ModifyTag(MachineCassettePlayer.CASETTE_PLAYER_STATE_INT,t => t.Enable().SetInt(state));
            if (tracks!=null && tracks.Count>0) machine.ModifyTag(MachineCassettePlayer.CURRENT_PLAYING_TRACK_ID_TAG,t => t.Enable().SetString(tracks[current]));
            if (bay!=null) foreach (var tape in bay.childItems.OfType<GameItemElement>()) tape.RefreshPlayingFrame();
            return true;
        }
    }
    [HarmonyPatch(typeof(MachineCassettePlayer),nameof(MachineCassettePlayer.OnPlayButtonPressed))]
    public static class TavernDeckPlay
    { private static bool Prefix(GameItem machine,GameGridInventory cassetteInventory) { return !(XiaomanMod.Instance?.TavernDeckCommand(machine,"play",cassetteInventory)??false); } }
    [HarmonyPatch(typeof(MachineCassettePlayer),nameof(MachineCassettePlayer.OnStopButtonPressed))]
    public static class TavernDeckStop
    { private static bool Prefix(GameItem machine) { return !(XiaomanMod.Instance?.TavernDeckCommand(machine,"stop")??false); } }
    [HarmonyPatch(typeof(MachineCassettePlayer),nameof(MachineCassettePlayer.OnNextButtonPressed))]
    public static class TavernDeckNext
    { private static bool Prefix(GameItem machine,GameGridInventory cassetteInventory) { return !(XiaomanMod.Instance?.TavernDeckCommand(machine,"next",cassetteInventory)??false); } }
    [HarmonyPatch(typeof(MachineCassettePlayer),nameof(MachineCassettePlayer.OnPreviousButtonPressed))]
    public static class TavernDeckPrevious
    { private static bool Prefix(GameItem machine,GameGridInventory cassetteInventory) { return !(XiaomanMod.Instance?.TavernDeckCommand(machine,"previous",cassetteInventory)??false); } }
    [HarmonyPatch(typeof(MachineCassettePlayer),nameof(MachineCassettePlayer.OnCassetteInventoryChanged))]
    public static class TavernDeckChanged
    { private static bool Prefix(GameItem machine,GameGridInventory cassetteInventory) { return !(XiaomanMod.Instance?.TavernDeckCommand(machine,"changed",cassetteInventory)??false); } }
}
