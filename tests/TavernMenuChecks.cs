using System;
using System.Reflection;
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
        Console.WriteLine("PASS: native choice menus yield rightward to speech; constrained layouts preserve speech and backpack access.");
    }
}
