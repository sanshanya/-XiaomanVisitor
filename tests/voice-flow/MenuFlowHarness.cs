using System;
using XiaomanVisitor;

// Only menu lifecycle and input availability are simulated. TavernMenuAction is compiled verbatim
// from the runtime source, so these checks exercise the production delegate.
namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        internal long tavernMenuRevision=1;
        internal bool TavernMayAct=true,TavernDialogueBusy,tavernActionBusy;
        internal Action CaptureMenu(Action action) { return TavernMenuAction(action); }
    }
}
internal static class MenuFlowHarness
{
    private static void Require(bool ok,string message)
    { if (!ok) throw new InvalidOperationException(message); }
    private static void Rejected(Action<XiaomanMod> change,string label)
    {
        var mod=new XiaomanMod();
        int effects=0;var old=mod.CaptureMenu(() => effects++);
        change(mod);old();
        Require(effects==0,"stale menu action executed: "+label);
    }
    internal static void Run()
    {
        Rejected(m => m.tavernMenuRevision++,"invalidated menu");
        Rejected(m => m.TavernMayAct=false,"left tavern or disabled mod");
        Rejected(m => m.TavernDialogueBusy=true,"dialogue or work owns input");
        Rejected(m => m.tavernActionBusy=true,"transaction in progress");
        var current=new XiaomanMod();
        int effects=0;var click=current.CaptureMenu(() => { effects++;current.tavernMenuRevision++; });
        click();click();
        Require(effects==1,"a menu-closing purchase or signature ran twice");
        var choices=new XiaomanMod();
        int first=0,second=0;
        var oldChoice=choices.CaptureMenu(() => { first++;choices.tavernMenuRevision++; });
        oldChoice();
        var newChoice=choices.CaptureMenu(() => second++);
        oldChoice();newChoice();
        Require(first==1 && second==1,"old choice bubbles interfered with their replacement");
        Console.WriteLine("PASS: production menu lifetime invalidation, input ownership, fresh choices and duplicate closing actions (no game launch).");
    }
}
