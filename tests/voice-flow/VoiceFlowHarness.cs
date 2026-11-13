using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using XiaomanVisitor;

// Test doubles cover playback, Unity activation and coroutine scheduling only.
// WrapFlow/WaitFlow/DeferEnd/WaitEnd/DeferAction/WaitAction/ResetConversation
// are taken verbatim from the production player by check-voice-flow.ps1.
public sealed class Dialogue
{
    public Action endAction;
    public bool isEndActionExecuted;
    public void ExecuteEndAction()
    {
        if (DialogUIManager.Instance.voice.DeferAction(this)) return;
        if (!isEndActionExecuted && endAction != null) { isEndActionExecuted=true;endAction(); }
    }
}
public sealed class DialogUIManager
{
    public static DialogUIManager Instance;
    public Dialogue currentDialog;
    public bool isActiveAndEnabled=true;
    public XiaomanVoicePlayer voice;
    public int closed;
    public void EndConversation()
    {
        if (voice.DeferEnd(this)) return;
        currentDialog=null;closed++;voice.Stop();
    }
}
public static class ModHelper
{
    private static readonly List<IEnumerator> pending=new List<IEnumerator>();
    public static int Count { get { return pending.Count; } }
    public static void StartCoroutine(string id,IEnumerator task) { if (task.MoveNext()) pending.Add(task); }
    public static void StopAllCoroutines(string id) { pending.Clear(); }
    public static void Tick(bool reverse)
    {
        var tasks=pending.ToArray();if (reverse) Array.Reverse(tasks);
        foreach (var task in tasks) if (pending.Contains(task) && !task.MoveNext()) pending.Remove(task);
    }
}
namespace XiaomanVisitor
{
    public sealed class TestSetting { public bool Value=true; }
    public sealed class XiaomanMod
    {
        public const string Id="sansm.xiaoman.visitor";
        public readonly TestSetting VoiceWait=new TestSetting();
        public bool recorded=true;
        public object RecordingFor(Dialogue line) { return recorded?line:null; }
    }
    public partial class XiaomanVoicePlayer
    {
        internal XiaomanMod mod=new XiaomanMod();
        internal Dialogue speaking,voicedLine;
        internal bool playing=true;
        private readonly HashSet<Dialogue> pendingActions=new HashSet<Dialogue>();
        private readonly HashSet<Dialogue> pendingEnds=new HashSet<Dialogue>();
        internal bool BusyFor(Dialogue line) { return line!=null && line==speaking && playing; }
        internal void Stop() { playing=false;speaking=null; }
    }
}
internal static class VoiceFlowHarness
{
    private static int scenarios;
    private static void Require(bool condition,string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Drain(bool reverse)
    {
        for (int n=0;n<8 && ModHelper.Count>0;n++) ModHelper.Tick(reverse);
        Require(ModHelper.Count==0,"speech wait was left pending");
    }
    private static DialogUIManager Create(out Dialogue line,out Action autoClose)
    {
        ModHelper.StopAllCoroutines(XiaomanMod.Id);
        line=new Dialogue();var voice=new XiaomanVoicePlayer { speaking=line,voicedLine=line };
        var owner=new DialogUIManager { currentDialog=line,voice=voice };DialogUIManager.Instance=owner;
        autoClose=owner.EndConversation;voice.WrapFlow(owner,ref autoClose);
        return owner;
    }
    private static void Collection(bool reverse,bool autoFirst,bool skip,bool disableWait)
    {
        Dialogue line;Action autoClose;var owner=Create(out line,out autoClose);
        int opened=0;bool introduced=false;
        line.endAction=() => { introduced=true;opened++; };
        if (autoFirst) autoClose();
        // Native OnCurrentTextDisplayed ends the bubble before executing its action.
        owner.EndConversation();line.ExecuteEndAction();
        if (!autoFirst) autoClose();
        Require(!introduced && owner.closed==0,"recording was not allowed to finish");
        if (skip) { owner.voice.Stop();owner.EndConversation();line.ExecuteEndAction(); }
        else if (disableWait) owner.voice.mod.VoiceWait.Value=false;
        else owner.voice.playing=false;
        Drain(reverse);
        Require(introduced && opened==1,"collection table never opened exactly once after audio ended");
        Require(line.isEndActionExecuted && owner.closed==1,"terminal dialogue did not close exactly once");
        line.ExecuteEndAction();Drain(reverse);
        Require(opened==1,"native delayed callback duplicated the collection form");
        scenarios++;
    }
    private static void Abandoned(bool reverse,bool reset,bool hidden)
    {
        Dialogue line;Action autoClose;var owner=Create(out line,out autoClose);int opened=0;
        line.endAction=() => opened++;
        owner.EndConversation();line.ExecuteEndAction();autoClose();
        var replacement=new Dialogue();
        if (reset) owner.voice.ResetConversation();
        else if (hidden) owner.isActiveAndEnabled=false;
        else owner.currentDialog=replacement;
        owner.voice.Stop();Drain(reverse);
        Require(opened==0 && owner.closed==0,"abandoned speech executed an old trade callback");
        if (!reset && !hidden) Require(owner.currentDialog==replacement,"old wait closed a new conversation");
        scenarios++;
    }
    private static void ActionStartsDialogue(bool reverse)
    {
        Dialogue line;Action autoClose;var owner=Create(out line,out autoClose);var replacement=new Dialogue();int opened=0;
        line.endAction=() => { opened++;owner.currentDialog=replacement; };
        owner.EndConversation();line.ExecuteEndAction();autoClose();owner.voice.playing=false;Drain(reverse);
        Require(opened==1 && owner.currentDialog==replacement && owner.closed==0,"completion closed its replacement dialogue");
        scenarios++;
    }
    private static void NativeFallback(bool reverse)
    {
        Dialogue line;Action autoClose;var owner=Create(out line,out autoClose);int opened=0;
        owner.voice.Stop();owner.voice.mod.recorded=false;
        line.endAction=() => opened++;
        owner.EndConversation();line.ExecuteEndAction();Drain(reverse);
        Require(opened==1 && owner.closed==1,"unvoiced native completion changed");scenarios++;
    }
    private static void Advance(bool reverse)
    {
        Dialogue line;Action autoClose;var owner=Create(out line,out autoClose);int completed=0;
        var next=new Dialogue();line.endAction=() => completed++;
        Action advance=() => { Require(completed==1,"advance discarded its queued end action");owner.currentDialog=next; };
        owner.voice.WrapFlow(owner,ref advance);
        advance();line.ExecuteEndAction();owner.voice.playing=false;Drain(reverse);
        Require(completed==1 && owner.currentDialog==next && owner.closed==0,"nonterminal advance changed");scenarios++;
    }
    private static void CloseWithoutAction(bool reverse)
    {
        Dialogue line;Action autoClose;var owner=Create(out line,out autoClose);
        owner.EndConversation();autoClose();owner.voice.playing=false;Drain(reverse);
        Require(owner.currentDialog==null && owner.closed==1,"voiced line without an end action did not close");scenarios++;
    }
    private static int Main()
    {
        foreach (bool reverse in new[]{false,true}) {
            foreach (bool autoFirst in new[]{false,true}) {
                Collection(reverse,autoFirst,false,false);
                Collection(reverse,autoFirst,true,false);
                Collection(reverse,autoFirst,false,true);
            }
            Abandoned(reverse,false,false);Abandoned(reverse,true,false);Abandoned(reverse,false,true);
            ActionStartsDialogue(reverse);NativeFallback(reverse);Advance(reverse);CloseWithoutAction(reverse);
        }
        Console.WriteLine("PASS: "+scenarios+" production voice-wait schedules; listen/skip, automatic close ordering, disabled wait, interrupted/reloaded/hidden contexts and native fallback (no game or audio playback).");
        return 0;
    }
}
