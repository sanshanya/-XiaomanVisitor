using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

internal static class CheckBuybackTest
{
    private static string managed, package;
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
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        return Run();
    }
    private static int Run()
    {
        string error;
        var manifest = ModLoader.ParseManifestForTools(package, out error);
        if (manifest == null || manifest.ID != "sansm.xiaoman.buyback_test")
            throw new Exception(error ?? "Wrong independent mod identity");
        var scan = ModScanner.Scan(manifest.Assemblies.Select(n => Path.Combine(package, n)).ToArray(),
            System.IO.Directory.GetFiles(managed, "*.dll").Select(Path.GetFileNameWithoutExtension).ToArray(), package, true);
        Console.WriteLine("Native scanner: capabilities=" + scan.Capabilities.Count + "; pending=" + scan.Pending.Count
            + "; hard=" + scan.HardFlags.Count + "; refused=" + scan.Refused);
        if (scan.Refused || scan.Error != null || scan.Capabilities.Count != 0 || scan.Pending.Count != 0 || scan.HardFlags.Count != 0)
            throw new Exception(scan.Error ?? scan.FormatFindings(30) + string.Join(";", scan.Pending));
        var assembly = Assembly.LoadFrom(Path.Combine(package, "XiaomanBuybackTest.dll"));
        if (assembly.GetTypes().Count(t => typeof(IMod).IsAssignableFrom(t)) != 1
            || assembly.GetTypes().Any(t => t.FullName == "XiaomanVisitor.XiaomanMod"))
            throw new Exception("Production entry point leaked into rehearsal");
        foreach (var target in new[] {
            new { Type = typeof(StoreBedroll), Method = "OnClick" },
            new { Type = typeof(EndOfDayUIManager), Method = "OnEndDayButtonClick" },
            new { Type = typeof(InputActionManager), Method = "Update" },
            new { Type = typeof(StoreUIManager), Method = "Update" },
            new { Type = typeof(DialogUIManager), Method = "OnAdvancePressed" } })
            if (target.Type.GetMethod(target.Method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null) == null) throw new Exception("Native patch target changed: " + target.Method);
        var options = new JsonSerializerOptions { IncludeFields = true };
        Type storyType = assembly.GetType("XiaomanVisitor.SideStories.BuybackScript", true);
        Type assetType = assembly.GetType("XiaomanVisitor.SideStories.BuybackAssets", true);
        var story = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(package, "Scenes/script.json")), storyType, options);
        var assets = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(package, "Scenes/assets.json")), assetType, options);
        var validate = assembly.GetType("XiaomanVisitor.SideStories.BuybackStage", true)
            .GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic);
        validate.Invoke(null, new[] { story, assets });
        Array cues = (Array)storyType.GetField("cues").GetValue(story);
        object attack = cues.Cast<object>().Single(c => (string)c.GetType().GetField("kind").GetValue(c) == "impact");
        var duration = attack.GetType().GetField("seconds");
        duration.SetValue(attack, float.NaN);
        ExpectRejected(validate, story, assets, "NaN timing");
        duration.SetValue(attack, 1.2f);
        object image = cues.Cast<object>().First(c => (string)c.GetType().GetField("kind").GetValue(c) == "image");
        image.GetType().GetField("value").SetValue(image, 3);
        ExpectRejected(validate, story, assets, "out-of-order CG");
        if (System.IO.Directory.GetFiles(package, "*.dll").Length != 1 || System.IO.Directory.Exists(Path.Combine(package, "Sounds"))
            || System.IO.Directory.Exists(Path.Combine(package, "Portraits")) || System.IO.Directory.Exists(Path.Combine(package, "Books")))
            throw new Exception("Unexpected production resources or audio in independent package");
        Console.WriteLine("PASS: native hooks, five-act data validation/rejection and isolated mod; gameplay was not launched.");
        return 0;
    }
    private static void ExpectRejected(MethodInfo validate, object story, object assets, string reason)
    {
        try { validate.Invoke(null, new[] { story, assets }); }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { return; }
        throw new Exception("Malformed timeline accepted: " + reason);
    }
}
