using System;
using System.IO;
using System.Linq;
using System.Reflection;

internal static class CheckGunTest
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
        ModManifest manifest = ModLoader.ParseManifestForTools(package, out error);
        if (manifest == null || manifest.ID != "sansm.xiaoman.gun_test") throw new Exception(error ?? "Wrong test ID");
        var scan = ModScanner.Scan(manifest.Assemblies.Select(n => Path.Combine(package, n)).ToArray(),
            System.IO.Directory.GetFiles(managed, "*.dll").Select(Path.GetFileNameWithoutExtension).ToArray(), package, true);
        Console.WriteLine("Test manifest: " + manifest.ID + "; capabilities=" + scan.Capabilities.Count + "; pending=" + scan.Pending.Count);
        if (scan.Refused || scan.Error != null || scan.Capabilities.Count != 0 || scan.Pending.Count != 0)
            throw new Exception(scan.Error ?? scan.FormatFindings(20) + string.Join(";", scan.Pending));
        Assembly assembly = Assembly.LoadFrom(Path.Combine(package, "XiaomanGunTest.dll"));
        Type mod = assembly.GetType("XiaomanVisitor.XiaomanMod", true);
        foreach (string name in new[] { "Id", "ClientId", "SpriteKey" })
            if (!((string)mod.GetField(name).GetRawConstantValue()).StartsWith("sansm.xiaoman.gun_test"))
                throw new Exception("Test identity overlaps production: " + name);
        foreach (string absent in new[] { "StoryProgress", "CassetteCollection", "CocktailCatalog", "XiaomanVoicePlayer", "XiaomanTavernStage" })
            if (assembly.GetType("XiaomanVisitor." + absent) != null) throw new Exception("Unexpected production module in test: " + absent);
        foreach (string present in new[] { "GunReaction", "XiaomanGunCanShootPatch", "XiaomanGunShootPatch", "XiaomanGunResumePatch", "XiaomanNativeRendering" })
            if (assembly.GetType("XiaomanVisitor." + present) == null) throw new Exception("Missing real reaction module: " + present);
        if (System.IO.Directory.GetFiles(package, "*.dll").Length != 1 || System.IO.Directory.Exists(Path.Combine(package, "Sounds")))
            throw new Exception("Test should ship one assembly and no music/voice");
        Console.WriteLine("PASS: independent mod/client/sprite IDs, real gun/rendering modules, no story/gift/tavern/audio modules; native scan accepted.");
        return 0;
    }
}
