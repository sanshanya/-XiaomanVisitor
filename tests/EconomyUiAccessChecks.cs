using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using XiaomanVisitor;

internal static class EconomyUiAccessChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var type=typeof(XiaomanMod);
        const BindingFlags instance=BindingFlags.Instance|BindingFlags.NonPublic;
        const BindingFlags constant=BindingFlags.Static|BindingFlags.NonPublic;
        bool defaultValue=(bool)type.GetField("EconomyLedgerDefault",constant).GetRawConstantValue();
        check(!defaultValue && (string)type.GetField("EconomyLedgerConfigKey",constant).GetRawConstantValue()=="show_economy_ledger",
            "ledger is a separate stable, default-off setting; the tavern's default-on setting is preserved");
        var setting=(ConfigEntry<bool>)Activator.CreateInstance(typeof(ConfigEntry<bool>),instance,null,new object[]{defaultValue},null);
        var mod=new XiaomanMod();
        type.GetField("enabled",instance).SetValue(mod,true);
        type.GetField("tavernEnabledForSession",instance).SetValue(mod,true);
        type.GetField("showEconomyLedger",instance).SetValue(mod,setting);
        var visible=type.GetProperty("EconomyLedgerVisible",instance);
        check(!setting.Default && !(bool)visible.GetValue(mod),"fresh settings do not expose the ledger");
        setting.Set(true);
        check((bool)visible.GetValue(mod),"explicit ledger opt-in takes effect without changing the tavern session");
        setting.Set(false);
        check(!(bool)visible.GetValue(mod),"opting out immediately prevents further ledger access");
        var account=new TavernEconomyProgress();account.Initialize(1,8,false);string before=account.Data;
        type.GetField("economyCache",instance).SetValue(mod,account);
        type.GetMethod("ShowEconomyWindow",instance).Invoke(mod,new object[]{"ledger"});
        check(ReferenceEquals(type.GetField("economyCache",instance).GetValue(mod),account) && account.Data==before,
            "a disabled ledger cannot replace or mutate an existing account while opening");
        setting.Set(true);type.GetField("tavernEnabledForSession",instance).SetValue(mod,false);
        check(setting.Value && !(bool)visible.GetValue(mod),"tavern opt-out blocks ledger access while retaining its saved opt-in");
        var gate=visible.GetGetMethod(true);
        foreach (string method in new[]{"ShowEconomyWindow","EnsureEconomyPhone","StartEconomyProject","PlayStayEnding","MakeEconomyPayment"})
            check(PatchProcessor.GetOriginalInstructions(type.GetMethod(method,instance)).Any(i => i.Calls(gate)),
                "ledger setting guards the actual runtime entry: "+method);
        check(PatchProcessor.GetOriginalInstructions(type.GetProperty("EconomyPickupLabel",instance).GetGetMethod(true)).Any(i => i.Calls(gate)),
            "pickup hotspot cannot bypass the hidden ledger");
        check(!PatchProcessor.GetOriginalInstructions(type.GetProperty("EconomyAvailable",instance).GetGetMethod(true)).Any(i => i.Calls(gate)),
            "the display switch does not disable financial progress or reinterpret a saved ending");
        var mayUse=type.GetMethod("MayUseEconomyWindow",BindingFlags.Static|BindingFlags.NonPublic);
        var first=new CustomUIWindow();var second=new CustomUIWindow();
        Func<CustomUIWindow,CustomUIWindow,string,string,int,int,bool> matches=(owner,current,openedRun,currentRun,openedDay,currentDay)
            => (bool)mayUse.Invoke(null,new object[]{owner,current,openedRun,currentRun,openedDay,currentDay});
        check(matches(first,first,"save-a","save-a",24,24),"the current window and save may submit on their captured day");
        check(!matches(first,null,"save-a","save-a",24,24) && !matches(null,null,"save-a","save-a",24,24),
            "closing invalidates old buttons and input callbacks even before native controls finish destroying");
        check(!matches(first,second,"save-a","save-a",24,24),"changing pages cannot let an old input change the new page's amount");
        check(!matches(first,first,"save-a","save-b",24,24) && !matches(first,first,"","",24,24),
            "a same-day different save and an unknown run cannot inherit callbacks");
        check(!matches(first,first,"save-a","save-a",24,25),"a stale callback cannot apply a previous day's action");
        var closeCode=PatchProcessor.GetOriginalInstructions(type.GetMethod("CloseEconomyWindow",instance)).ToArray();
        int invalidate=Array.FindIndex(closeCode,i => Equals(i.operand,type.GetField("economyWindow",instance)) && i.opcode==System.Reflection.Emit.OpCodes.Stfld);
        int close=Array.FindIndex(closeCode,i => i.Calls(AccessTools.Method(typeof(CustomUIManager),nameof(CustomUIManager.CloseWindow))));
        check(invalidate>=0 && close>invalidate,"actual runtime invalidates window ownership before native close can emit input events");
    }
}
