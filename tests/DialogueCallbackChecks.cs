using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using XiaomanVisitor;

internal static class DialogueCallbackChecks
{
    public static void Run(Action<bool,string> check)
    {
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var type=typeof(XiaomanMod);
        var gate=type.GetMethod("MayFinishCallback",BindingFlags.Static|BindingFlags.NonPublic);
        Func<object,object,string,string,int,int,bool> current=(owner,actual,run,now,day,today) =>
            (bool)gate.Invoke(null,new object[]{owner,actual,run,now,day,today,0L,0L});
        var first=new StoreClient { identifier="same-client-id" };
        var replacement=new StoreClient { identifier="same-client-id" };
        check(current(first,first,"run-a","run-a",24,24),"the visitor that created a callback may finish its current visit");
        check(!current(first,replacement,"run-a","run-a",24,24),"a visitor with the same ID after reload cannot inherit an old action");
        check(!current(first,null,"run-a","run-a",24,24) && !current(null,null,"run-a","run-a",24,24),
            "departure and absent owners cannot open a trade form or dismiss a later customer");
        check(!current(first,first,"run-a","run-b",24,24) && !current(first,first,"","",24,24),
            "different and unknown save identities reject old business callbacks");
        check(!current(first,first,"run-a","run-a",24,25),"a past day's callback cannot perform today's business");
        var phone=new StorePhoneClient { dialogFuncId="same-phone-id" };
        var anotherPhone=new StorePhoneClient { dialogFuncId="same-phone-id" };
        check(current(phone,phone,"run-a","run-a",24,24) && !current(phone,anotherPhone,"run-a","run-a",24,24),
            "phone completions belong to the actual call object, not just its dialogue ID");
        check((bool)gate.Invoke(null,new object[]{phone,phone,"run-a","run-a",24,24,1L,1L})
            && !(bool)gate.Invoke(null,new object[]{phone,phone,"run-a","run-a",24,24,1L,2L}),
            "redialing the same phone contact invalidates the earlier call's completion");
        var visitorGuard=type.GetMethod("VisitorAction",flags);
        foreach (string entry in new[]{"ConfigureStoryOpening","CreateBrewingVisitor","CreateWorkThanksVisitor","BuybackAcceptance","BuybackReturn","SupportVisitor"})
            check(PatchProcessor.GetOriginalInstructions(type.GetMethod(entry,flags)).Any(i=>i.Calls(visitorGuard)),
                "the actual visitor introduction/transaction uses the captured context: "+entry);

        // Exercise the real night-dialogue cancellation without a Unity stage.
        var mod=new XiaomanMod();
        var lines=type.GetField("tavernLines",flags).GetValue(mod);
        lines.GetType().GetMethod("Enqueue").Invoke(lines,new object[]{"old-night-line"});
        type.GetField("tavernSequence",flags).SetValue(mod,true);
        type.GetField("tavernInputBlocked",flags).SetValue(mod,true);
        bool completed=false;
        type.GetField("tavernDialogueDone",flags).SetValue(mod,(Action)(()=>completed=true));
        type.GetMethod("CancelTavernSpeech",flags).Invoke(mod,null);
        check((int)lines.GetType().GetProperty("Count").GetValue(lines)==0 && !(bool)type.GetField("tavernSequence",flags).GetValue(mod)
            && !(bool)type.GetField("tavernInputBlocked",flags).GetValue(mod)
            && type.GetField("tavernDialogueDone",flags).GetValue(mod)==null && !completed,
            "load/leave cancellation clears queued night lines and abandons their consequences");
        check(PatchProcessor.GetOriginalInstructions(type.GetMethod("OnGameLoaded",flags))
            .Any(i=>i.Calls(type.GetMethod("CancelTavernSpeech",flags))),
            "the actual load hook cancels night speech as well as shop voice waits");
        Console.WriteLine("PASS: visitor/call/save/day ownership and night-speech cancellation; native post-bubble business callbacks stay scoped.");
    }
}
