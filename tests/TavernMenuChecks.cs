using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using XiaomanVisitor;

internal static class TavernMenuChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var method=typeof(XiaomanMod).Assembly.GetType("XiaomanVisitor.XiaomanTavernChoices")
            .GetMethod("Place",BindingFlags.NonPublic|BindingFlags.Static);
        Func<Rect?,float,Vector2,Rect> menu=(speech,bottom,size) => {
            var point=(Vector2)method.Invoke(null,new object[] { new Vector2(1920,1080),new Vector2(1030,575),size,speech,bottom,16f });
            return new Rect(point,size);
        };
        var shortLine=new Rect(960,600,280,84);
        var right=menu(shortLine,1080,new Vector2(180,124));
        check(right.xMin>=shortLine.xMax+16 && right.xMax<=1904 && !right.Overlaps(shortLine),
            "short greeting keeps its anchor; choices move right of its actual bounds");
        var longLine=new Rect(960,600,700,140);
        var below=menu(longLine,1080,new Vector2(260,180));
        check(below.yMin>=longLine.yMax+16 && below.xMax<=1904 && !below.Overlaps(longLine),
            "when the right column cannot fit, only the menu moves below the long line");
        var above=menu(longLine,760,new Vector2(260,180));
        check(above.yMax<=longLine.yMin-16 && above.yMin>=16 && !above.Overlaps(longLine),
            "an open backpack keeps the menu above speech instead of shifting speech left or covering the bag");
        var idle=menu(null,1080,new Vector2(180,124));
        check(idle.xMin==1046 && idle.yMin==575,"speech-free choices retain their normal actor-side anchor");
        CheckCallbackBindings(check);
        Console.WriteLine("PASS: native choice menus yield rightward to speech; constrained layouts preserve speech and backpack access.");
    }
    private static void CheckCallbackBindings(Action<bool,string> check)
    {
        var type=typeof(XiaomanMod);const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var guard=type.GetMethod("TavernMenuAction",flags);
        foreach (string entry in new[]{"BuildTavernDrinkMenu","ShowTavernChoices","BuildSupplyMenu","ShowTavernMenu"}) {
            var callbacks=type.GetNestedTypes(BindingFlags.NonPublic).SelectMany(t => t.GetMethods(flags))
                .Where(m => m.Name.StartsWith("<"+entry+">",StringComparison.Ordinal));
            check(new[]{type.GetMethod(entry,flags)}.Concat(callbacks)
                .Any(m => PatchProcessor.GetOriginalInstructions(m).Any(i => i.Calls(guard))),
                "production buttons capture the current menu context: "+entry);
        }
        var revision=type.GetField("tavernMenuRevision",flags);
        var close=type.GetMethod("CloseTavernMenu",flags);
        var code=PatchProcessor.GetOriginalInstructions(close).ToArray();
        int invalidate=Array.FindIndex(code,i => i.opcode==OpCodes.Stfld && Equals(i.operand,revision));
        int destroy=Array.FindIndex(code,i => i.Calls(AccessTools.Method(typeof(CustomUIManager),nameof(CustomUIManager.CloseWindow))));
        check(invalidate>=0 && destroy>invalidate,"closing invalidates queued menu clicks before native controls are destroyed");
        foreach (string entry in new[]{"OnGameLoaded","LeaveTavern","ShowTavernChoices"})
            check(PatchProcessor.GetOriginalInstructions(type.GetMethod(entry,flags)).Any(i => i.Calls(close)),
                "load, leave and choice replacement invalidate the old menu: "+entry);
        var rebuild=PatchProcessor.GetOriginalInstructions(type.GetMethod("ShowTavernMenu",flags)).ToArray();
        int fresh=Array.FindIndex(rebuild,i => i.opcode==OpCodes.Stfld && Equals(i.operand,revision));
        int clear=Array.FindIndex(rebuild,i => i.Calls(AccessTools.Method(typeof(CustomUIBuilder),nameof(CustomUIBuilder.ClearContainer))));
        check(fresh>=0 && clear>fresh,"refreshing the same window invalidates its old controls before rebuilding");
    }
}
