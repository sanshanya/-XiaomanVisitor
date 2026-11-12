using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using HarmonyLib;

internal static class CheckBuybackTest
{
    private static string managed, package, replay;
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        string path = Path.Combine(managed, name);
        if (!File.Exists(path)) path = Path.Combine(package, name);
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
    private static int Main(string[] args)
    {
        managed = args[0]; package = args[1];
        replay = args.Length > 2 ? args[2] : null;
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        return Run();
    }
    private static int Run()
    {
        string error;
        var manifest = ModLoader.ParseManifestForTools(package, out error);
        if (manifest == null || manifest.ID != "sansm.xiaoman.visitor")
            throw new Exception(error ?? "Wrong main mod identity");
        var scan = ModScanner.Scan(manifest.Assemblies.Select(n => Path.Combine(package, n)).ToArray(),
            System.IO.Directory.GetFiles(managed, "*.dll").Select(Path.GetFileNameWithoutExtension).ToArray(), package, true);
        Console.WriteLine("Native scanner: capabilities=" + scan.Capabilities.Count + "; pending=" + scan.Pending.Count
            + "; hard=" + scan.HardFlags.Count + "; refused=" + scan.Refused);
        if (scan.Refused || scan.Error != null || scan.Capabilities.Count != 0 || scan.Pending.Count != 0 || scan.HardFlags.Count != 0)
            throw new Exception(scan.Error ?? scan.FormatFindings(30) + string.Join(";", scan.Pending));
        var assembly = Assembly.LoadFrom(Path.Combine(package, "XiaomanVisitor.dll"));
        var visitorType = assembly.GetType("XiaomanVisitor.XiaomanMod", true);
        if (assembly.GetTypes().Count(t => typeof(IMod).IsAssignableFrom(t)) != 1
            || !typeof(IMod).IsAssignableFrom(visitorType)
            || (string)visitorType.GetField("Id").GetRawConstantValue() != "sansm.xiaoman.visitor")
            throw new Exception("The main package must have exactly one IMod entry and preserve its original identity");
        foreach (var target in new[] {
            new { Type = typeof(StoreBedroll), Method = "OnClick" },
            new { Type = typeof(EndOfDayUIManager), Method = "OnEndDayButtonClick" },
            new { Type = typeof(InputActionManager), Method = "Update" },
            new { Type = typeof(StoreUIManager), Method = "Update" },
            new { Type = typeof(DialogUIManager), Method = "OnAdvancePressed" },
            new { Type = typeof(NewsUIManager), Method = "PopulateUI" } })
            if (target.Type.GetMethod(target.Method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null) == null) throw new Exception("Native patch target changed: " + target.Method);
        Type storyType = assembly.GetType("XiaomanVisitor.SideStories.BuybackScript", true);
        Type assetType = assembly.GetType("XiaomanVisitor.SideStories.BuybackAssets", true);
        Type stageType = assembly.GetType("XiaomanVisitor.SideStories.BuybackStage", true);
        var parser = stageType.GetMethod("ParseData", BindingFlags.Static | BindingFlags.NonPublic);
        var parseStory = parser.MakeGenericMethod(storyType);
        var parseAssets = parser.MakeGenericMethod(assetType);
        var story = parseStory.Invoke(null, new object[] { File.ReadAllText(Path.Combine(package, "Scenes/Buyback/script.json")) });
        var assets = parseAssets.Invoke(null, new object[] { File.ReadAllText(Path.Combine(package, "Scenes/Buyback/assets.json")) });
        var validate = stageType
            .GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic);
        validate.Invoke(null, new[] { story, assets });
        CheckRefusalBranch(assembly, parser);
        CheckWaterPrelude(assembly, parser);
        CheckMusicLifetime(assembly);
        CheckVoices(assembly, parser, story);
        var dialogueMode = storyType.GetField("dialogue_mode");
        dialogueMode.SetValue(story, "auto");
        ExpectRejected(validate, story, assets, "automatic dialogue in the manual performance");
        dialogueMode.SetValue(story, "manual");
        // Re-read BOM/CRLF input through the runtime parser; broken JSON must not turn into empty fields.
        var withBom = parseStory.Invoke(null, new object[] { "\uFEFF" + File.ReadAllText(Path.Combine(package, "Scenes/Buyback/script.json")) });
        validate.Invoke(null, new[] { withBom, assets });
        try { parseStory.Invoke(null, new object[] { "{" }); throw new Exception("Broken JSON was silently accepted"); }
        catch (TargetInvocationException failure) when (failure.InnerException is InvalidOperationException) { }
        if (replay != null)
        {
            var oldStory = parseStory.Invoke(null, new object[] { File.ReadAllText(Path.Combine(replay, "script.json")) });
            var oldAssets = parseAssets.Invoke(null, new object[] { File.ReadAllText(Path.Combine(replay, "assets.json")) });
            int count = ((Array)storyType.GetField("cues").GetValue(oldStory)).Length;
            int images = ((Array)assetType.GetField("images").GetValue(oldAssets)).Length;
            if ((int)storyType.GetField("schema").GetValue(oldStory) != 1
                || (string)storyType.GetField("language").GetValue(oldStory) != "zh" || count != 139 || images != 5)
                throw new Exception("Actual failed-install data could not be recovered");
            Console.WriteLine("PASS: actual failed-install JSON replayed by runtime parser: 139 cues, five CGs.");
        }
        Console.WriteLine("PASS: shared runtime JSON parser, BOM/CRLF input and malformed-data rejection.");
        var buildLens = assembly.GetType("XiaomanVisitor.SideStories.BuybackLensGraphic", true)
            .GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic);
        var meshExport = new List<object>();
        for (int style = 0; style < 3; style++)
        {
            using var mesh = new VertexHelper();
            buildLens.Invoke(null, new object[] { mesh, new Rect(0, 0, 1920, 1080), style });
            if (mesh.currentVertCount == 0 || mesh.currentVertCount > 3000)
                throw new Exception("Unexpected lens geometry budget");
            var points = new List<float[]>();
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                var vertex = new UIVertex(); mesh.PopulateUIVertex(ref vertex, i);
                if (float.IsNaN(vertex.position.x) || float.IsNaN(vertex.position.y))
                    throw new Exception("Invalid lens vertex");
                if (style != 2 && vertex.color.a > 0 && new Rect(691, 410, 538, 453).Contains(vertex.position))
                    throw new Exception("Blood obscures the central face area");
                points.Add(new float[] { vertex.position.x, vertex.position.y,
                    vertex.color.r, vertex.color.g, vertex.color.b, vertex.color.a });
            }
            // Export the managed vertex/index lists, without calling Unity's native renderer.
            var indexField = typeof(VertexHelper).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(f => f.FieldType == typeof(List<int>));
            var indices = ((List<int>)indexField.GetValue(mesh)).ToArray();
            meshExport.Add(new { style, vertices=mesh.currentVertCount,
                points=points.ToArray(), indices });
        }
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(typeof(CheckBuybackTest).Assembly.Location), "lens-mesh.json"),
            JsonSerializer.Serialize(meshExport));
        Console.WriteLine("PASS: actual UI lens meshes, bounded geometry and clear central face area (no game launch).");
        var beatMesh = assembly.GetType("XiaomanVisitor.SideStories.BuybackLensGraphic", true)
            .GetMethod("BuildBeat", BindingFlags.Static | BindingFlags.NonPublic);
        int previousBloodVertices = -1;
        var bloodBeatExport = new List<object>();
        for (int beat = 0; beat <= 3; beat++)
        {
            using var mesh = new VertexHelper();
            beatMesh.Invoke(null, new object[] { mesh, new Rect(0, 0, 1920, 1080), 0, beat });
            if ((beat == 0 && mesh.currentVertCount != 0) || mesh.currentVertCount <= previousBloodVertices || mesh.currentVertCount > 3000)
                throw new Exception("Blood must accumulate in three distinct, bounded bursts");
            previousBloodVertices = mesh.currentVertCount;
            var points = new List<float[]>();
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                var vertex = new UIVertex(); mesh.PopulateUIVertex(ref vertex, i);
                points.Add(new float[] { vertex.position.x, vertex.position.y, vertex.color.r, vertex.color.g, vertex.color.b, vertex.color.a });
            }
            var indexField = typeof(VertexHelper).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(f => f.FieldType == typeof(List<int>));
            bloodBeatExport.Add(new { beat, points = points.ToArray(), indices = ((List<int>)indexField.GetValue(mesh))?.ToArray() ?? Array.Empty<int>() });
        }
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(typeof(CheckBuybackTest).Assembly.Location), "blood-beats.json"), JsonSerializer.Serialize(bloodBeatExport));
        Console.WriteLine("PASS: actual blood meshes start empty and accumulate across exactly three bursts.");
        var buildShadow = assembly.GetType("XiaomanVisitor.SideStories.BuybackDoorShadeGraphic", true)
            .GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic);
        var shadowExport = new List<object>();
        for (int step = 0; step <= 16; step++)
        {
            using var mesh = new VertexHelper();
            float t = step / 16f, progress = t * t * (3 - 2 * t);
            buildShadow.Invoke(null, new object[] { mesh, new Rect(0, 0, 1086, 1448), progress });
            if (mesh.currentVertCount > 8 || (step == 0 && mesh.currentVertCount != 4)
                || (step == 16 && mesh.currentVertCount != 0)) throw new Exception("Invalid door shadow endpoints/budget");
            var points = new List<float[]>();
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                var vertex = new UIVertex(); mesh.PopulateUIVertex(ref vertex, i);
                if (float.IsNaN(vertex.position.x) || vertex.position.x < 0 || vertex.position.x > 1086
                    || (step == 0 && vertex.color.a != 255) || (step == 8 && vertex.position.x < 400))
                    throw new Exception("Door shadow must start opaque and clear from the left");
                points.Add(new float[] { vertex.position.x, vertex.position.y, vertex.color.a });
            }
            shadowExport.Add(new { progress, points=points.ToArray() });
        }
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(typeof(CheckBuybackTest).Assembly.Location), "door-shadow-mesh.json"),
            JsonSerializer.Serialize(shadowExport));
        Console.WriteLine("PASS: actual soft-edge door shadow geometry, opaque/clear endpoints and left-to-right reveal.");
        Array cues = (Array)storyType.GetField("cues").GetValue(story);
        object knock = cues.Cast<object>().Single(c => (string)c.GetType().GetField("kind").GetValue(c) == "knock");
        knock.GetType().GetField("value").SetValue(knock, 1);
        ExpectRejected(validate, story, assets, "knock sequence must be requested three times");
        knock.GetType().GetField("value").SetValue(knock, 3);
        object attack = cues.Cast<object>().Single(c => (string)c.GetType().GetField("kind").GetValue(c) == "impact");
        var duration = attack.GetType().GetField("seconds");
        duration.SetValue(attack, float.NaN);
        ExpectRejected(validate, story, assets, "NaN timing");
        duration.SetValue(attack, 1.2f);
        object image = cues.Cast<object>().First(c => (string)c.GetType().GetField("kind").GetValue(c) == "image");
        var transition = image.GetType().GetField("transition");
        transition.SetValue(image, "flash_loop");
        ExpectRejected(validate, story, assets, "unapproved repetitive transition");
        transition.SetValue(image, "door_wipe");
        image.GetType().GetField("value").SetValue(image, 3);
        ExpectRejected(validate, story, assets, "out-of-order CG");
        image.GetType().GetField("value").SetValue(image, 1);
        object choice = cues.Cast<object>().Single(c => (string)c.GetType().GetField("kind").GetValue(c) == "door_choice");
        choice.GetType().GetField("kind").SetValue(choice, "wait");
        ExpectRejected(validate, story, assets, "missing door decision before CG");
        choice.GetType().GetField("kind").SetValue(choice, "door_choice");
        object reply = cues.Cast<object>().Single(c => (string)c.GetType().GetField("kind").GetValue(c) == "reply_choice");
        reply.GetType().GetField("options").SetValue(reply, new[] { "美", "很美" });
        ExpectRejected(validate, story, assets, "question must preserve the player's silence");
        reply.GetType().GetField("options").SetValue(reply, new[] { "……", "……" });
        validate.Invoke(null, new[] { story, assets });
        var sounds = System.IO.Directory.GetFiles(Path.Combine(package, "Sounds"), "*", SearchOption.AllDirectories);
        if (!sounds.Select(Path.GetFileName).Contains("buyback_door_knock.wav") || !sounds.Select(Path.GetFileName).Contains("rain_intro.mp3"))
            throw new Exception("Recorded door knocks and approved Intro must be packaged");
        var music = assetType.GetField("music").GetValue(assets);
        string expectedMusicHash = (string)music.GetType().GetField("sha256").GetValue(music);
        string musicHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            File.ReadAllBytes(Path.Combine(package, "Sounds/rain_intro.mp3")))).ToLowerInvariant();
        if (musicHash != expectedMusicHash) throw new Exception("Packaged Intro differs from approved audio");
        if (System.IO.Directory.GetFiles(package, "*.dll").Length != 1
            || !System.IO.Directory.Exists(Path.Combine(package, "Portraits")))
            throw new Exception("Test must reuse the visitor portraits in one assembly");
        var books = System.IO.Directory.GetFiles(Path.Combine(package, "Books"), "*.json");
        var note = (PaperBookData)parser.MakeGenericMethod(typeof(PaperBookData)).Invoke(null,
            new object[] { File.ReadAllText(books.Single(p => Path.GetFileName(p) == "last_note.json")) });
        if (note.item.id != "sansm.xiaoman.visitor.last_note" || note.AllElements().Single(e => e.id == "photo").type != "image"
            || !note.AllElements().Single(e => e.id == "message").text.Contains("我们终将重逢！！！"))
            throw new Exception("Photo note content/identity is invalid");
        if (note.AllElements().Any(e => e.action == "ending") || note.background != "sansm.xiaoman.visitor:xiaoman_letter_paper")
            throw new Exception("Last letter must use Xiaoman paper and must not end the run");
        var isLetter = assembly.GetType("XiaomanVisitor.NotebookRules", true).GetMethod("IsLetter");
        if (!(bool)isLetter.Invoke(null, new object[] { note.item.id })) throw new Exception("Last letter cannot enter Xiaoman's notebook");
        if (AccessTools.Method(typeof(RunSummaryUI), "EndingTitle") == null || AccessTools.Method(typeof(GameOverUIManager), "DisplayUI") == null)
            throw new Exception("Native ending UI changed");
        var endPrefix = assembly.GetType("XiaomanVisitor.SideStories.BuybackEndingTitlePatch", true).GetMethod("Prefix");
        object[] endingArgs = { "sansm.xiaoman.visitor.ending.buyback", "" };
        if ((bool)endPrefix.Invoke(null, endingArgs) || (string)endingArgs[1] != "永久回购") throw new Exception("Custom ending title missing");
        Console.WriteLine("PASS: native hooks, five-act data validation/rejection and main mod; gameplay was not launched.");
        return 0;
    }
    private static void ExpectRejected(MethodInfo validate, object story, object assets, string reason)
    {
        try { validate.Invoke(null, new[] { story, assets }); }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { return; }
        throw new Exception("Malformed timeline accepted: " + reason);
    }

    private static void CheckRefusalBranch(Assembly assembly, MethodInfo parser)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        var type = assembly.GetType("XiaomanVisitor.SideStories.BuybackDoorProgress", true);
        var restore = type.GetMethod("Restore", statics);
        var refuse = type.GetMethod("Refuse", instance);
        var retry = type.GetMethod("NeedsRetry", instance);
        var news = type.GetMethod("HasNews", instance);
        var data = type.GetProperty("Data", instance);
        object state = restore.Invoke(null, new object[] { "" });
        if ((bool)news.Invoke(state, new object[] { 2 })) throw new Exception("Unchosen refusal produced news");
        refuse.Invoke(state, new object[] { 1 });
        string firstSnapshot = (string)data.GetValue(state);
        state = restore.Invoke(null, new object[] { firstSnapshot });
        if (!(bool)retry.Invoke(state, new object[] { 1 }) || (bool)news.Invoke(state, new object[] { 2 }))
            throw new Exception("First refusal must return to the same night's retry, without news");
        refuse.Invoke(state, new object[] { 1 });
        string secondSnapshot = (string)data.GetValue(state);
        state = restore.Invoke(null, new object[] { secondSnapshot });
        if (!(bool)retry.Invoke(state, new object[] { 1 }) || (bool)news.Invoke(state, new object[] { 2 }))
            throw new Exception("Second refusal must still allow the third door wave");
        refuse.Invoke(state, new object[] { 1 });
        refuse.Invoke(state, new object[] { 1 }); // Repeated delivery must not create a fourth refusal.
        state = restore.Invoke(null, new object[] { (string)data.GetValue(state) });
        if ((bool)retry.Invoke(state, new object[] { 1 }) || (bool)news.Invoke(state, new object[] { 1 })
            || !(bool)news.Invoke(state, new object[] { 2 }) || (bool)news.Invoke(state, new object[] { 3 }))
            throw new Exception("Third refusal news must survive reload and appear only the following day");
        var needsNote = type.GetMethod("NeedsNote", instance);
        if (!(bool)needsNote.Invoke(state, new object[] { 2 })) throw new Exception("Photo note missing after refusal ending");
        type.GetField("noteDelivered").SetValue(state, true);
        state = restore.Invoke(null, new object[] { (string)data.GetValue(state) });
        if ((bool)needsNote.Invoke(state, new object[] { 2 })) throw new Exception("Photo note would be duplicated");
        var legacy = restore.Invoke(null, new object[] { "{\"schema\":1,\"day\":1,\"refusals\":2}" });
        if (!(bool)news.Invoke(legacy, new object[] { 2 })) throw new Exception("Legacy completed refusal ending was lost");
        state = restore.Invoke(null, new object[] { firstSnapshot });
        type.GetMethod("Open", instance).Invoke(state, null);
        state = restore.Invoke(null, new object[] { (string)data.GetValue(state) });
        if ((bool)retry.Invoke(state, new object[] { 1 }) || (bool)news.Invoke(state, new object[] { 2 }))
            throw new Exception("Opening on retry must cancel refusal news");
        try { restore.Invoke(null, new object[] { "{\"schema\":1,\"day\":-1,\"refusals\":2}" }); throw new Exception("Invalid refusal state accepted"); }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }

        var textType = assembly.GetType("XiaomanVisitor.SideStories.BuybackRefusalText", true);
        var text = parser.MakeGenericMethod(textType).Invoke(null, new object[] { File.ReadAllText(Path.Combine(package, "Scenes/Buyback/refusal.json")) });
        textType.GetMethod("Validate", instance).Invoke(text, null);
        var newspaper = assembly.GetType("XiaomanVisitor.SideStories.BuybackNewspaper", true);
        var article = (StoreEvent)newspaper.GetMethod("Article", statics).Invoke(null, new[] { text, (object)2 });
        var append = newspaper.GetMethod("Append", statics);
        var empty = (List<StoreEvent>)append.Invoke(null, new object[] { new List<StoreEvent>(), article });
        if (empty.Count != 1 || !ReferenceEquals(empty[0], article)) throw new Exception("Next-day article missing");
        var native = Enumerable.Range(0, 4).Select(i => new StoreEvent { identifier = "native_" + i,
            newsName = "Native title " + i, newsDescription = "Native body " + i }).ToList();
        var display = (List<StoreEvent>)append.Invoke(null, new object[] { native, article });
        if (ReferenceEquals(display, native) || display.Count != 4 || native[3].newsDescription != "Native body 3"
            || !display[3].newsDescription.Contains("Native body 3") || !display[3].newsDescription.Contains(article.newsDescription)
            || !ReferenceEquals(display[3].negociationDatas, native[3].negociationDatas)
            || !ReferenceEquals(append.Invoke(null, new object[] { display, article }), display))
            throw new Exception("Full newspaper must preserve native text/data and avoid duplicates");
        var original = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(NewsUIManager), "PopulateUI"));
        var nativeRead = AccessTools.Method(typeof(StoreEventManager), "GetActiveEvents");
        if (original.Count(c => c.Calls(nativeRead)) != 1) throw new Exception("Native newspaper call shape changed");
        var patch = assembly.GetType("XiaomanVisitor.XiaomanNewspaperPopulatePatch", true).GetMethod("Transpiler");
        var patched = ((IEnumerable<CodeInstruction>)patch.Invoke(null, new object[] { original })).ToArray();
        var singleAdapter = assembly.GetType("XiaomanVisitor.XiaomanNewspaper", true).GetMethod("GetDisplayEvents", statics);
        if (patched.Any(c => c.Calls(nativeRead)) || patched.Count(c => c.Calls(singleAdapter)) != 1)
            throw new Exception("Native NEWS read was not redirected to the display-only adapter");
        if (!PatchProcessor.GetOriginalInstructions(singleAdapter).Any(c => c.Calls(newspaper.GetMethod("Append", statics))))
            throw new Exception("The single NEWS adapter omits refusal news");
        Console.WriteLine("PASS: persisted three-wave refusal/open branches, legacy ending migration, unique photo note, next-day NEWS and actual native NEWS IL hook.");
    }

    private static void CheckWaterPrelude(Assembly assembly, MethodInfo parser)
    {
        var policy = assembly.GetType("XiaomanVisitor.BuybackProgress", true).GetMethod("CanRecordSale");
        if (!(bool)policy.Invoke(null, new object[] { 1, false })
            || !(bool)policy.Invoke(null, new object[] { 2, false })
            || (bool)policy.Invoke(null, new object[] { 0, false })
            || (bool)policy.Invoke(null, new object[] { 1, true }))
            throw new Exception("Special-water sales must be eligible from day one, before tavern opening");
        var availability = assembly.GetType("XiaomanVisitor.XiaomanMod", true)
            .GetProperty("BuybackTradingAvailable", BindingFlags.Instance | BindingFlags.NonPublic).GetGetMethod(true);
        if (!PatchProcessor.GetOriginalInstructions(availability).Any(c => c.Calls(policy)))
            throw new Exception("Runtime eligibility must use the tested day-one sale policy");
        var progressType = assembly.GetType("XiaomanVisitor.BuybackProgress", true);
        var firstDay = Activator.CreateInstance(progressType);
        progressType.GetMethod("Record").Invoke(firstDay, new object[] { 1, 901 });
        var firstReturn = progressType.GetMethod("ReturnDue");
        if ((bool)firstReturn.Invoke(firstDay, new object[] { 1 }) || !(bool)firstReturn.Invoke(firstDay, new object[] { 2 }))
            throw new Exception("A first-day sale must schedule the next day, not the second week");
        firstDay = progressType.GetMethod("Restore").Invoke(null,
            new object[] { (string)progressType.GetProperty("Data").GetValue(firstDay) });
        if (!(bool)firstReturn.Invoke(firstDay, new object[] { 2 }))
            throw new Exception("Day-one water return was lost after saving and reloading");
        Console.WriteLine("PASS: actual runtime sale gate accepts day one; first purchase schedules day-two return and survives reload.");
        var type = assembly.GetType("XiaomanVisitor.BuybackProgress", true);
        var restore = type.GetMethod("Restore");
        var record = type.GetMethod("Record"); var meet = type.GetMethod("MeetReturn");
        var due = type.GetMethod("ReturnDue"); var night = type.GetMethod("NightReady");
        object state = restore.Invoke(null, new object[] { "" });
        if ((bool)night.Invoke(state, new object[] { 1 })) throw new Exception("Unqualified prelude triggered");
        if ((int)record.Invoke(state, new object[] { 8, 101 }) != 1 || (int)record.Invoke(state, new object[] { 8, 101 }) != 0)
            throw new Exception("A repeated sale callback counted twice");
        if ((bool)due.Invoke(state, new object[] { 8 }) || !(bool)due.Invoke(state, new object[] { 9 })
            || (bool)night.Invoke(state, new object[] { 9 })) throw new Exception("First purchase did not schedule just a next-day return");
        meet.Invoke(state, new object[] { 9 });
        if ((bool)due.Invoke(state, new object[] { 9 }) || (bool)night.Invoke(state, new object[] { 9 }))
            throw new Exception("First return repeated or armed the ending");
        if ((int)record.Invoke(state, new object[] { 9, 102 }) != 2) throw new Exception("Second distinct receipt lost");
        state = restore.Invoke(null, new[] { type.GetProperty("Data").GetValue(state) });
        if ((bool)night.Invoke(state, new object[] { 9 }) || !(bool)due.Invoke(state, new object[] { 10 }))
            throw new Exception("Second purchase must wait for the following day's visit");
        if (!(bool)meet.Invoke(state, new object[] { 10 }) || (bool)meet.Invoke(state, new object[] { 10 })
            || !(bool)night.Invoke(state, new object[] { 10 })) throw new Exception("Second return is not idempotent/eligible at night");
        if ((int)record.Invoke(state, new object[] { 10, 103 }) != 0 || !(bool)night.Invoke(state, new object[] { 10 }))
            throw new Exception("Extra purchases moved the fixed second-sale receipt");
        object missed = restore.Invoke(null, new object[] { "" }); record.Invoke(missed, new object[] { 8, 201 });
        if (!(bool)due.Invoke(missed, new object[] { 12 })) throw new Exception("A closed shop lost its pending visitor");
        meet.Invoke(missed, new object[] { 12 });
        if ((bool)due.Invoke(missed, new object[] { 12 })) throw new Exception("Late return repeated on the same day");
        try {
            restore.Invoke(null, new object[] { "{\"Schema\":1,\"Sales\":[{\"Day\":8,\"ItemId\":101},{\"Day\":9,\"ItemId\":101}]}" });
            throw new Exception("Duplicate saved receipts silently accepted");
        } catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { }
        const BindingFlags nonpublic = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic;
        var mod = assembly.GetType("XiaomanVisitor.SideStories.BuybackStory", true);
        var owns = mod.GetMethod("OwnsVisitor", nonpublic);
        var visitorId = (string)assembly.GetType("XiaomanVisitor.XiaomanMod", true).GetField("ClientId").GetRawConstantValue();
        var ownClient = new StoreClient { identifier = visitorId, sourceModId = "sansm.xiaoman.visitor" };
        var otherClient = new StoreClient { identifier = visitorId, sourceModId = "another.mod" };
        if (!(bool)owns.Invoke(null, new object[] { ownClient }) || (bool)owns.Invoke(null, new object[] { otherClient })
            || (bool)owns.Invoke(null, new object[] { null }))
            throw new Exception("Queue removal and sale receipts must require the exact source mod, not just a matching character ID");
        var drugWater = PatchProcessor.GetOriginalInstructions(mod.GetMethod("IsDrugWater", nonpublic));
        var waterDose = mod.GetMethod("WaterDose", nonpublic);
        var dosePolicy = progressType.GetMethod("HasEffectiveWaterDose");
        if (!drugWater.Any(c => c.Calls(dosePolicy)) || !drugWater.Any(c => c.Calls(waterDose))
            || drugWater.Any(c => c.Calls(AccessTools.Method(typeof(ChemicalFeedbackHelper), nameof(ChemicalFeedbackHelper.IsNarcotic)))))
            throw new Exception("Xiaoman must use her own low-dose rule, without changing native customer thresholds");
        foreach (double dose in new[] { 0.0, 19.999, double.NaN, double.PositiveInfinity })
            if ((bool)dosePolicy.Invoke(null, new object[] { dose })) throw new Exception("Invalid/subthreshold dose accepted: " + dose);
        foreach (double dose in new[] { 20.0, 20.001, 433.0, 750.0 })
            if (!(bool)dosePolicy.Invoke(null, new object[] { dose })) throw new Exception("Xiaoman dose rejected: " + dose);
        if ((double)progressType.GetField("MinimumWaterDose").GetRawConstantValue() != 20)
            throw new Exception("Xiaoman's authored threshold must remain 20");
        Console.WriteLine("PASS: exact 20-point threshold, 19.999 rejection, real 433-point bottle acceptance and non-finite rejection; runtime uses its own policy.");
        var queue = PatchProcessor.GetOriginalInstructions(mod.GetMethod("HandleVisitQueue", nonpublic));
        if (!queue.Any(c => c.Calls(AccessTools.Method(typeof(StoreClientManager), nameof(StoreClientManager.AddClient))))
            || queue.Any(c => c.Calls(AccessTools.Method(typeof(StoreClientManager), nameof(StoreClientManager.AddNextClient)))))
            throw new Exception("Return must append, never insert ahead of native clients");
        var texts = parser.MakeGenericMethod(assembly.GetType("XiaomanVisitor.SideStories.BuybackPreludeText", true))
            .Invoke(null, new object[] { File.ReadAllText(Path.Combine(package, "Scenes/Buyback/prelude.json")) });
        texts.GetType().GetMethod("Validate", nonpublic).Invoke(texts, null);
        if (((string[])texts.GetType().GetField("second_accept").GetValue(texts)).Length < 3)
            throw new Exception("Second-water dialogue missing");
        var stage = assembly.GetType("XiaomanVisitor.SideStories.BuybackStage", true);
        var labelCode = PatchProcessor.GetOriginalInstructions(stage.GetMethod("Label", nonpublic));
        var choiceText = typeof(DialogChoiceBubble).GetField("TMP");
        if (!labelCode.Any(c => Equals(c.operand, choiceText))
            || labelCode.Any(c => Equals(c.operand, typeof(TextBubble).GetField("textfield"))))
            throw new Exception("Static labels still copy animated speech text");
        Console.WriteLine("PASS: actual sale receipts/reload/dedup, both next-day returns, missed-visit carryover, night eligibility, native chemical/queue bindings and static choice-label template.");
    }

    private static void CheckVoices(Assembly assembly, MethodInfo parser, object story)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var documentType = assembly.GetType("XiaomanVisitor.VoicePackDocument", true);
        var document = parser.MakeGenericMethod(documentType).Invoke(null,
            new object[] { File.ReadAllText(Path.Combine(package, "Voices/index.json")) });
        var rows = (Array)documentType.GetField("lines").GetValue(document);
        var catalogType = assembly.GetType("XiaomanVisitor.VoiceCatalog", true);
        var catalog = catalogType.GetConstructors().Single().Invoke(new object[] { rows });
        var resolve = catalogType.GetMethod("Resolve");
        Action<string, string> require = (key, subtitle) => {
            if (resolve.Invoke(catalog, new object[] { key, "ja", subtitle }) == null
                || resolve.Invoke(catalog, new object[] { key, "ja", "CHANGED " + subtitle }) != null)
                throw new Exception("Missing or inexact ending voice binding: " + key);
        };
        int count = 0;
        foreach (var cue in (Array)story.GetType().GetField("cues").GetValue(story)) {
            var t = cue.GetType();
            if ((string)t.GetField("kind").GetValue(cue) != "line") continue;
            require((string)t.GetField("key").GetValue(cue), (string)t.GetField("text").GetValue(cue)); count++;
        }
        var preludeType = assembly.GetType("XiaomanVisitor.SideStories.BuybackPreludeText", true);
        var prelude = parser.MakeGenericMethod(preludeType).Invoke(null,
            new object[] { File.ReadAllText(Path.Combine(package, "Scenes/Buyback/prelude.json")) });
        var mod = assembly.GetType("XiaomanVisitor.SideStories.BuybackStory", true);
        var keyFor = mod.GetMethod("PreludeVoiceKey", flags, null, new[] { preludeType, typeof(string) }, null);
        foreach (var field in preludeType.GetFields()) {
            if (field.FieldType != typeof(string) && field.FieldType != typeof(string[])) continue;
            var values = field.FieldType == typeof(string) ? new[] { (string)field.GetValue(prelude) }
                : (string[])field.GetValue(prelude);
            foreach (string text in values) {
                require((string)keyFor.Invoke(null, new[] { prelude, (object)text }), text); count++;
            }
        }
        if (keyFor.Invoke(null, new[] { prelude, (object)"其他顾客的相似台词" }) != null)
            throw new Exception("Unowned dialogue acquired a prelude voice");
        var refusalType = assembly.GetType("XiaomanVisitor.SideStories.BuybackRefusalText", true);
        var refusal = parser.MakeGenericMethod(refusalType).Invoke(null,
            new object[] { File.ReadAllText(Path.Combine(package, "Scenes/Buyback/refusal.json")) });
        foreach (string name in new[] { "retry_line", "farewell_line" }) {
            require("buyback_refusal_" + name, (string)refusalType.GetField(name).GetValue(refusal)); count++;
        }
        var third = (string[])refusalType.GetField("third_lines").GetValue(refusal);
        for (int i = 0; i < third.Length; i++) { require("buyback_refusal_third_lines_" + i, third[i]); count++; }
        if (count != 147) throw new Exception("Ending voice coverage changed: " + count);
        Console.WriteLine("PASS: runtime voice catalog resolves all 147 exact five-act/prelude/refusal subtitles, including delayed returns; changed/unowned text stays silent.");
    }

    private static void CheckMusicLifetime(Assembly assembly)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = assembly.GetType("XiaomanVisitor.SideStories.BuybackMusic", true);
        object music = Activator.CreateInstance(type, instance, null, new object[] { null }, null);
        type.GetField("radioRun", instance).SetValue(music, "test_run");
        type.GetField("radioDay", instance).SetValue(music, 1);
        type.GetMethod("RetainUntilMorning", instance).Invoke(music, null);
        var continues = type.GetMethod("CanContinue", instance);
        for (int i = 0; i < 3; i++)
        {
            type.GetMethod("ReleaseStage", instance).Invoke(music, new object[] { false });
            if (!(bool)continues.Invoke(music, new object[] { "test_run", 1 })) throw new Exception("Refusal closed the night's music lifetime");
        }
        type.GetMethod("OnMorning", instance).Invoke(music, new object[] { "test_run", 1 });
        if (!(bool)continues.Invoke(music, new object[] { "test_run", 1 })) throw new Exception("Music released before the next day");
        type.GetMethod("OnMorning", instance).Invoke(music, new object[] { "test_run", 2 });
        if ((bool)continues.Invoke(music, new object[] { "test_run", 1 })) throw new Exception("Music did not release next morning");
        if (typeof(ModHook).GetEvent("OnWakingUpLate") == null) throw new Exception("Native morning hook changed");
        Console.WriteLine("PASS: actual music lifetime retained over three stage releases/retries and released next morning (no audio playback).");
    }
}
