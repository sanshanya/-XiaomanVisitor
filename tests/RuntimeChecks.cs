using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using XiaomanVisitor;
using UnityEngine.U2D;

internal static class CheckMod
{
    private static string managed, package;
    private static int assertions;
    private static int Main(string[] args)
    {
        managed = args[0]; package = args[1];
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        return Run();
    }
    private static int Run()
    {
        string problem;
        ModManifest manifest = ModLoader.ParseManifestForTools(package, out problem);
        if (manifest == null) { Console.Error.WriteLine(problem); return 1; }
        var scan = ModScanner.Scan(manifest.Assemblies.Select(a => Path.Combine(package, a)).ToArray(),
            System.IO.Directory.GetFiles(managed, "*.dll").Select(Path.GetFileNameWithoutExtension).ToArray(), package, true);
        Console.WriteLine("Manifest: " + manifest.ID + " v" + manifest.ModVersion + "; methods: " + scan.MethodsScanned);
        Console.WriteLine("Native scanner: capabilities=" + (scan.Capabilities.Count == 0 ? "none" : string.Join(",", scan.Capabilities))
            + "; pending=" + scan.Pending.Count + "; refused=" + scan.Refused);
        if (scan.Error != null) Console.Error.WriteLine(scan.Error);
        if (scan.Findings.Count > 0 || scan.HardFlags.Count > 0) Console.WriteLine(scan.FormatFindings(30));
        foreach (var finding in scan.Pending.Take(30))
            Console.Error.WriteLine("PENDING (next API enforcement): " + finding);
        if (scan.Refused || scan.Capabilities.Except(manifest.Capabilities).Any()) return 2;
        if (scan.Pending.Count > 0) return 2;
        CheckStory.Run(package, Require); WeekOneChecks.Run(package, Require); CheckDrinks(); CheckBuffs(); CheckNativeBindings(); CheckVoices(); CheckPortraitTiming(); CheckPaper(); CheckNotebook(); CheckCassetteCase(); CheckTavernMap(); CheckTavernProgress(); CheckPortraitDetail(); CheckShopPortrait(); GunReactionChecks.Run(package, Require); WeekFourChecks.Run(package,Require); WeekFiveSixChecks.Run(Require);
        Console.WriteLine("PASS: " + assertions + " assertions; native manifest, capability scanner and paper parser accepted.");
        return 0;
    }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string p = Path.Combine(managed, new AssemblyName(args.Name).Name + ".dll");
        if (!File.Exists(p)) p = Path.Combine(package, new AssemblyName(args.Name).Name + ".dll");
        return File.Exists(p) ? Assembly.LoadFrom(p) : null;
    }
    private static void Require(bool ok, string label)
    { assertions++; if (!ok) throw new InvalidOperationException("Check failed: " + label); }
    private static DrinkRewardProgress Reload(DrinkRewardProgress p)
    { return DrinkRewardProgress.Restore(p.PendingData, p.DeliveredData, p.LastDeliveryDay, (int)p.LastDeliveredEffect); }
    private static VisitTradeSummary Sale(int n, long paid, long quote, long fair, bool discount)
    { var v = new VisitTradeSummary(); v.Record(n, paid, quote, fair, discount); return v; }
    private static VisitTradeSummary Deal(int quantity, int credits, int discount)
    {
        var v = new VisitTradeSummary();
        v.Record(quantity, credits, 1000 - discount * 10, 1000, discount > 0, discount);
        return v;
    }

    private static void CheckDrinks()
    {
        Require(CocktailCatalog.Drinks.Length == 6 && CocktailCatalog.Drinks.Select(d => d.ItemId).Distinct().Count() == 6, "six drink items");
        Require(CocktailCatalog.Drinks.All(d => d.Width == 16 && d.Height == 32
            && File.Exists(Path.Combine(package, "Textures", "Items", d.SpriteKey + ".png"))), "1x2 artwork retained");
        Require(DrinkRewardRules.Select(Deal(2, 99, 9)) == null, "below all three thresholds gives no drink");
        Require(DrinkRewardRules.Select(Deal(1, 50, 10)) == "rainy_blue", "10 percent base reputation drink");
        Require(DrinkRewardRules.Select(Deal(1, 50, 24)) == "rainy_blue", "below strong discount threshold");
        Require(DrinkRewardRules.Select(Deal(1, 50, 25)) == "sky_after_rain", "25 percent strong reputation drink");
        Require(DrinkRewardRules.Select(Deal(3, 50, 0)) == "whale_dream", "three units base scavenging drink");
        Require(DrinkRewardRules.Select(Deal(5, 50, 0)) == "whale_dream", "below strong quantity threshold");
        Require(DrinkRewardRules.Select(Deal(6, 50, 0)) == "cosmo_zone", "six units strong scavenging drink");
        Require(DrinkRewardRules.Select(Deal(1, 100, 0)) == "sunset_blue", "100 paid credits base luck drink");
        Require(DrinkRewardRules.Select(Deal(1, 199, 0)) == "sunset_blue", "below strong credits threshold");
        Require(DrinkRewardRules.Select(Deal(1, 200, 0)) == "bluebird", "200 paid credits strong luck drink");
        Require(DrinkRewardRules.Select(Deal(6, 199, 24)) == "cosmo_zone", "strong quantity beats overachieved basic discount and credits");
        Require(DrinkRewardRules.Select(Deal(3, 200, 10)) == "bluebird", "basic discount cannot block strong credits");
        Require(DrinkRewardRules.Select(Deal(9, 200, 25)) == "cosmo_zone", "relative completion breaks same-tier overlap");
        Require(DrinkRewardRules.Select(Deal(6, 250, 25)) == "bluebird", "greater relative credit achievement");
        Require(DrinkRewardRules.Select(Deal(6, 200, 50)) == "sky_after_rain", "greater relative discount achievement");
        Require(DrinkRewardRules.Select(Deal(6, 200, 25)) == "sky_after_rain", "first exact tie starts at discount");
        Require(DrinkRewardRules.Select(Deal(6, 200, 25), DrinkEffect.TradeReputation) == "cosmo_zone", "tie rotates to quantity");
        Require(DrinkRewardRules.Select(Deal(6, 200, 25), DrinkEffect.Scavenging) == "bluebird", "tie rotates to credits");
        Require(DrinkRewardRules.Select(Deal(6, 200, 25), DrinkEffect.Luck) == "sky_after_rain", "tie cycles back to discount");
        var rounding = new VisitTradeSummary();
        rounding.Record(1, 31, 31, 34, true, 10);
        Require(rounding.DiscountPercentExact == 10m && DrinkRewardRules.Select(rounding) == "rainy_blue", "native 10 percent survives integer price rounding");
        rounding = new VisitTradeSummary(); rounding.Record(1, 26, 26, 34, true, 25);
        Require(VisitTradeSummary.Restore(rounding.Data).DiscountPercentExact == 25m
            && DrinkRewardRules.Select(rounding) == "sky_after_rain", "native 25 percent survives rounding and save");
        rounding.Record(1, 34, 34, 34, false);
        Require(rounding.DiscountPercentExact == 12.5m, "mixed items use value-weighted whole-visit discount");
        Require(!Sale(1, 10, 100, 100, false).HasDiscount && DrinkRewardRules.Select(Sale(1, 10, 100, 100, false)) == null, "budget cap is not a discount");
        Require(!Sale(1, 5, 5, 5, true).HasDiscount, "cheap item is not a discount");
        Require(VisitTradeSummary.Restore("1,75,100,25").DiscountPercentExact == 25m, "old visit data remains readable");
        var visit = Sale(1, 30, 30, 30, false); var gifts = new DrinkRewardProgress();
        Require(!gifts.Update(1, visit) && gifts.Pending.Count == 0, "ordinary sale queues no drink");
        visit.Record(1, 30, 30, 30, false); visit = VisitTradeSummary.Restore(visit.Data);
        Require(visit.Quantity == 2 && visit.Credits == 60 && !gifts.Update(1, visit), "visit aggregates persist");
        visit.Record(1, 30, 30, 30, false); gifts.Update(1, visit);
        Require(gifts.Pending.Single().Key == "whale_dream", "third item qualifies base quantity drink");
        visit.Record(3, 30, 30, 30, false); gifts.Update(1, visit);
        Require(gifts.Pending.Single().Key == "cosmo_zone", "six units upgrade single pending drink");
        visit.Record(1, 180, 180, 180, false); gifts.Update(1, visit); gifts = Reload(gifts);
        Require(gifts.Pending.Single().Key == "bluebird" && gifts.Due(1) == null && gifts.Due(2) != null, "final totals decide one next-day drink without reroll");
        Require(!gifts.Deliver(1, 1) && gifts.Deliver(1, 2) && !gifts.Deliver(1, 2), "no early or repeated delivery");
        gifts = Reload(gifts);
        Require(!gifts.Update(1, visit) && gifts.LastDeliveredEffect == DrinkEffect.Luck, "reload retains last delivered group and prevents refill");
        Require(gifts.Update(3, Deal(6, 200, 25)) && gifts.Update(10, Deal(3, 30, 0)), "independent queued visits");
        gifts = Reload(gifts);
        Require(gifts.Pending.Count == 2 && gifts.Due(20).TradeDay == 3, "full counter preserves old gift");
        Require(gifts.Deliver(3, 20) && gifts.Due(20) == null && gifts.Due(21).TradeDay == 10, "at most one backlog drink daily");
        gifts = Reload(gifts);
        Require(gifts.LastDeliveredEffect == DrinkEffect.TradeReputation, "tie history changes on delivery, not repeated selection");
        Require(DrinkRewardProgress.Restore("bad,3,4;rainy_blue,3,4;bluebird,3,4;cosmo_zone,4,4", "", -1).Pending.Count == 1, "bad drink data cannot duplicate");
        foreach (DrinkEffect effect in new[] { DrinkEffect.Luck, DrinkEffect.TradeReputation, DrinkEffect.Scavenging })
            Require(CocktailCatalog.Drinks.Count(d => d.Effect == effect && d.Tier == 1) == 1
                && CocktailCatalog.Drinks.Count(d => d.Effect == effect && d.Tier == 2) == 1, "one basic and one strong per group");
        Console.WriteLine("PASS: 3 groups, 6 thresholds, strong-first selection, achievement scores, tie rotation, aggregates, rounding and next-day queue.");
    }
    private static void CheckBuffs()
    {
        var buff = new DrinkBuffProgress();
        foreach (var spec in CocktailCatalog.Drinks)
        {
            buff.Replace(spec, 5);
            Require(buff.UntilDay == 8 && buff.IsActive(5) && buff.IsActive(6) && buff.IsActive(7) && !buff.IsActive(8), "all six last exactly three calendar days");
            Require(buff.ActiveAmount(spec.Effect, 6) == spec.EffectAmount, "correct native effect amount");
        }
        buff.Replace(CocktailCatalog.Find("bluebird"), 5);
        Require(DrinkEffectMath.Luck(20, buff.ActiveAmount(DrinkEffect.Luck, 5)) == 28, "strong luck +8");
        buff.Replace(CocktailCatalog.Find("sunset_blue"), 6);
        Require(DrinkEffectMath.Luck(27, buff.ActiveAmount(DrinkEffect.Luck, 6)) == 32, "basic replaces strong and retains other luck");
        var loaded = new DrinkBuffProgress { Key = buff.Key, Effect = buff.Effect, Amount = buff.Amount, UntilDay = buff.UntilDay };
        Require(!loaded.Expire(8) && loaded.Expire(9) && !loaded.Expire(10), "reload and expiry cannot duplicate contribution");
        Require(DrinkEffectMath.Luck(27, loaded.ActiveAmount(DrinkEffect.Luck, 9)) == 27, "expiry leaves native luck intact");
        var combined = new DrinkBuffCollection();
        combined.Replace(CocktailCatalog.Find("sunset_blue"), 5);
        combined.Replace(CocktailCatalog.Find("rainy_blue"), 6);
        combined.Replace(CocktailCatalog.Find("whale_dream"), 7);
        Require(combined.Count == 3 && combined.ActiveAmount(DrinkEffect.Luck, 7) == 5
            && combined.ActiveAmount(DrinkEffect.TradeReputation, 7) == 10
            && combined.ActiveAmount(DrinkEffect.Scavenging, 7) == 2, "three different categories coexist");
        string combinedSave = combined.Data;
        combined = DrinkBuffCollection.Restore(combinedSave);
        Require(combined.Count == 3 && combined.ActiveAmount(DrinkEffect.Luck, 7) == 5
            && combined.ActiveAmount(DrinkEffect.TradeReputation, 7) == 10
            && combined.ActiveAmount(DrinkEffect.Scavenging, 7) == 2, "all categories survive save and reload");
        combined.Replace(CocktailCatalog.Find("bluebird"), 7);
        combined = DrinkBuffCollection.Restore(combined.Data);
        Require(combined.ActiveAmount(DrinkEffect.Luck, 8) == 8
            && combined.ActiveAmount(DrinkEffect.TradeReputation, 8) == 10
            && combined.ActiveAmount(DrinkEffect.Scavenging, 8) == 2, "same category upgrades without changing others");
        Require(combined.ActiveAmount(DrinkEffect.Luck, 9) == 8
            && combined.ActiveAmount(DrinkEffect.TradeReputation, 9) == 0
            && combined.ActiveAmount(DrinkEffect.Scavenging, 9) == 2, "only refreshed category gets a new expiry");
        combined.Replace(CocktailCatalog.Find("sunset_blue"), 8);
        Require(combined.ActiveAmount(DrinkEffect.Luck, 8) == 5
            && combined.ActiveAmount(DrinkEffect.Luck, 10) == 5
            && combined.ActiveAmount(DrinkEffect.Scavenging, 10) == 0, "same category replaces, never adds amounts or refreshes another timer");
        var expiry = DrinkBuffCollection.Restore(combinedSave);
        Require(expiry.Expire(8) && expiry.Count == 2 && expiry.ActiveAmount(DrinkEffect.Luck, 8) == 0
            && expiry.ActiveAmount(DrinkEffect.TradeReputation, 8) == 10, "luck expiry retains reputation and scavenging");
        Require(expiry.Expire(9) && expiry.Count == 1 && expiry.ActiveAmount(DrinkEffect.Scavenging, 9) == 2,
            "next category expires separately");
        Require(expiry.Expire(10) && expiry.Count == 0 && !expiry.Expire(11), "last expiry and repeat cleanup are idempotent");
        var legacy = new DrinkBuffCollection();
        legacy.Import(new DrinkBuffProgress { Key = "sky_after_rain", Effect = DrinkEffect.TradeReputation, Amount = 20, UntilDay = 10 });
        legacy.Replace(CocktailCatalog.Find("bluebird"), 9);
        legacy = DrinkBuffCollection.Restore(legacy.Data);
        Require(legacy.ActiveAmount(DrinkEffect.TradeReputation, 9) == 20
            && legacy.ActiveAmount(DrinkEffect.TradeReputation, 10) == 0
            && legacy.ActiveAmount(DrinkEffect.Luck, 10) == 8, "old single-category state retains its remaining time alongside new category");
        var duplicate = DrinkBuffCollection.Restore("1,sunset_blue,5,8;1,bluebird,8,8;0,bad,99,9;2,bad,-1,9;bad");
        Require(duplicate.Count == 1 && duplicate.ActiveAmount(DrinkEffect.Luck, 7) == 8, "duplicate and malformed rows cannot add the same category twice");
        buff.Replace(CocktailCatalog.Find("cosmo_zone"), 5);
        for (int day = 5; day <= 7; day++)
            Require(DrinkEffectMath.ScavengingLimit(5, buff.ActiveAmount(DrinkEffect.Scavenging, day)) == 9, "four extra attempts each day, not accumulated daily");
        Require(DrinkEffectMath.ScavengingLimit(5, 2) == 7 && DrinkEffectMath.ScavengingLimit(0, 4) == 0, "base scavenging plus native disabling traits respected");
        Require(DrinkEffectMath.ScavengingLimit(5, 4) - 7 == 2, "increasing cap retains spent attempts");
        Require(Math.Abs(DrinkEffectMath.TradeReputation(2.5, 10, true) - 2.75) < 1e-9
            && Math.Abs(DrinkEffectMath.TradeReputation(2.5, 20, true) - 3.0) < 1e-9, "10 and 20 percent reputation gains");
        Require(DrinkEffectMath.TradeReputation(-2.5, 20, true) == -2.5 && DrinkEffectMath.TradeReputation(2.5, 20, false) == 2.5,
            "reputation loss and non-trade awards unchanged");
        Require(!TradeRewardScope.IsActive, "outside trade context");
        int outer = TradeRewardScope.Enter();
        try
        {
            Require(TradeRewardScope.IsActive, "actual trade context");
            int inner = TradeRewardScope.Enter(); TradeRewardScope.Restore(inner);
            Require(TradeRewardScope.IsActive, "nested native calls keep outer trade");
            throw new InvalidOperationException("simulated native error");
        }
        catch (InvalidOperationException) { }
        finally { TradeRewardScope.Restore(outer); }
        Require(!TradeRewardScope.IsActive, "exception cleanup cannot leak bonus into later quest award");
        Require(CocktailCatalog.Drinks.All(d => d.DurationDays == 3), "uniform three-day duration");
        Console.WriteLine("PASS: 3-day effects, cross-category stacking, independent expiry, same-category replacement, reload, legacy state and trade scope cleanup.");
    }
    private static void CheckNativeBindings()
    {
        foreach (Type patch in typeof(XiaomanMod).Assembly.GetTypes().Where(t => t.IsDefined(typeof(HarmonyPatch),false)))
        {
            var attribute = (HarmonyPatch)patch.GetCustomAttributes(typeof(HarmonyPatch), false).Single();
            var info = attribute.info;
            var targets = info.declaringType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Where(m => m.Name == info.methodName && (info.argumentTypes == null || m.GetParameters().Select(p => p.ParameterType).SequenceEqual(info.argumentTypes))).ToArray();
            Require(targets.Length == 1 && (targets[0].DeclaringType.Assembly == typeof(GameItem).Assembly
                || patch == typeof(XiaomanTavernTypewriterSpeedPatch)
                    && info.declaringType.FullName == "Febucci.UI.Core.TypewriterCore"
                    && Path.GetFullPath(targets[0].DeclaringType.Assembly.Location)
                        == Path.GetFullPath(Path.Combine(managed, "Febucci.TextAnimator.Runtime.dll"))),
                "one exact installed-game method per patch: " + patch.Name);
        }
        Require(CocktailCatalog.Drinks.All(d => ItemPresentation.OwnsItemId(d.ItemId))
            && CassetteCollection.Tracks.All(t => ItemPresentation.OwnsItemId(t.ItemId))
            && ItemPresentation.OwnsItemId(XiaomanMod.Id + ".story_note_01")
            && ItemPresentation.OwnsItemId(XiaomanMod.Id + ".thank_you_note"), "only Xiaoman's item namespace hides source footers");
        Require(!ItemPresentation.OwnsItemId(null) && !ItemPresentation.OwnsItemId("water")
            && !ItemPresentation.OwnsItemId("other.mod.cocktail.sunset_blue")
            && !ItemPresentation.OwnsItemId(XiaomanMod.Id + "_other.tape"), "native and other mod items keep source tooltips");
        Console.WriteLine("PASS: native Harmony declarations bind uniquely; item source footers are scoped to Xiaoman's item namespace.");
    }
    private static void CheckVoices()
    {
        var pack = JsonConvert.DeserializeObject<VoicePackDocument>(File.ReadAllText(Path.Combine(package, "Voices", "index.json")));
        Require(pack != null && pack.schema == 1 && pack.lines.Length > 0, "offline voice pack present");
        var catalog = new VoiceCatalog(pack.lines);
        Require(catalog.Count == pack.lines.Length, "all packaged voice bindings are valid and unique");
        foreach (var row in pack.lines)
        {
            Require(File.Exists(Path.Combine(package, "Sounds", row.sound_key + ".wav")), "bound voice WAV exists");
            foreach (string template in row.subtitle_templates)
                Require(catalog.Resolve(row.line_key, row.language, template) != null, "same recording supports approved subtitle translations");
            Require(catalog.Resolve(row.line_key, row.language, row.subtitle_templates[0] + "changed") == null,
                "changed subtitle cannot silently reuse an old recording");
            Require(catalog.Resolve("other_customer", row.language, row.subtitle_templates[0]) == null,
                "matching text from another dialogue key does not pick Xiaoman voice");
        }
        var intro = pack.lines.Single(r => r.line_key == "intro_1" && r.language == "ja");
        Require(catalog.Resolve("intro_1", "ja-JP", intro.subtitle_templates[0]) != null
            && catalog.Resolve("intro_1", "zh-CN", intro.subtitle_templates[0]) == null, "regional language normalization without wrong-language fallback");
        Require(!typeof(XiaomanMod).Assembly.GetReferencedAssemblies().Any(a => a.Name == "ProbablyStolenVoiceLines"),
            "standalone mod has no required Station Voices assembly reference");
        for (int mask = 0; mask < 8; mask++)
            Require(VoiceCatalog.SuppressStationVoice((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0) == (mask == 7),
                "handoff affects only owned lines with both voice systems active");
        var budget = new VoiceRecording { line_key = "shopping", language = "ja", sound_key = "test_voice",
            spoken_text = "今日は少し買い物をしたくて。", subtitle_templates = new[] { "予算は{0}です。" }, generic = true };
        Require(new VoiceCatalog(new[] { budget }).Resolve("shopping", "ja", "予算は{0}です。") != null,
            "reviewed generic speech supports dynamic budgets");
        budget.generic = false;
        Require(new VoiceCatalog(new[] { budget }).Resolve("shopping", "ja", "予算は{0}です。") == null, "parameterized dialogue requires explicit generic speech");
        budget.spoken_text = "{0}です。";
        Require(new VoiceCatalog(new[] { budget }).Count == 0, "unresolved spoken placeholders cannot enter runtime pack");
        Require(!VoiceCatalog.SafeSoundKey("../recording") && !VoiceCatalog.SafeSoundKey("https://voice") && VoiceCatalog.SafeSoundKey("xm_voice_ja_intro_1"),
            "runtime accepts only packaged sound identifiers");
        Console.WriteLine("PASS: standalone offline voice, explicit keys, stale-text rejection, regional language, dynamic lines and optional Station Voices handoff.");
    }
    private static void CheckPortraitTiming()
    {
        DialoguePose pose = PortraitTiming.Resolve(true, true, Mood.Shy, true, true, false, 2, 3.0f);
        Require(pose.Speaking && pose.Expression == Mood.Shy && pose.Mouth == 2,
            "voice continues the mouth and this line's expression after subtitles finish");
        pose = PortraitTiming.Resolve(true, true, Mood.Worried, true, false, true, 2, 3.0f);
        Require(!pose.Speaking && pose.Expression == Mood.Neutral && pose.Mouth == 0,
            "finished, skipped or interrupted voice cannot restart from unfinished subtitles");
        pose = PortraitTiming.Resolve(true, true, Mood.Soft, true, true, false, 0, 3.0f);
        Require(pose.Speaking && pose.Expression == Mood.Soft && pose.Mouth == 0,
            "pause or intra-line silence closes the mouth while retaining the line's emotion");
        pose = PortraitTiming.Resolve(true, true, Mood.Worried, false, false, true, 0, 0.28f);
        Require(pose.Speaking && pose.Expression == Mood.Worried && pose.Mouth > 0,
            "missing or unavailable recordings retain subtitle-based animation");
        pose = PortraitTiming.Resolve(true, false, Mood.Closed, false, false, true, 0, 0.28f);
        Require(!pose.Speaking && pose.Mouth == 0, "knock captions never cause speech mouth frames");
        pose = PortraitTiming.Resolve(false, true, Mood.Smile, true, true, true, 2, 0.28f);
        Require(!pose.Speaking && pose.Mouth == 0 && pose.Expression == Mood.Neutral,
            "hidden, departed and other-customer dialogues cannot animate Xiaoman");
        Require(PortraitTiming.MayBlink(Mood.Neutral) && PortraitTiming.MayBlink(Mood.Soft)
            && !PortraitTiming.MayBlink(Mood.Shy) && !PortraitTiming.MayBlink(Mood.Worried)
            && !PortraitTiming.MayBlink(Mood.Smile) && !PortraitTiming.MayBlink(Mood.Closed),
            "generic blink does not replace an authored emotional expression");

        // Real gaps in a speech-shaped PCM signal, including opposite-phase stereo.
        const int frequency = 1000;
        float[] mono = new float[frequency * 3], stereo = new float[frequency * 6], quiet = new float[frequency];
        for (int i = 0; i < mono.Length; i++)
        {
            float seconds = (float)i / frequency;
            mono[i] = (seconds < 0.8f || (seconds >= 1.6f && seconds < 2.6f)) ? (float)(0.2 * Math.Sin(i * 0.7)) : 0;
            stereo[i * 2] = mono[i]; stereo[i * 2 + 1] = -mono[i];
        }
        for (int i = 0; i < quiet.Length; i++) quiet[i] = 0.001f;
        var envelope = SpeechEnvelope.FromSamples(mono, frequency, 1);
        var stereoEnvelope = SpeechEnvelope.FromSamples(stereo, frequency, 2);
        Require(envelope.MouthAt(0.28f) > 0 && envelope.MouthAt(1.2f) == 0 && envelope.MouthAt(2.12f) > 0,
            "mouth resumes with the second spoken phrase after a real pause");
        Require(envelope.MouthAt(2.8f) == 0 && envelope.MouthAt(3.0f) == 0,
            "trailing silence and completed audio use the original closed-mouth frame");
        Require(envelope.MouthAt(-1) == 0 && envelope.MouthAt(float.NaN) == 0 && envelope.MouthAt(float.PositiveInfinity) == 0,
            "invalid playback positions do not select a speaking frame");
        Require(SpeechEnvelope.FromSamples(new float[0], frequency, 1).MouthAt(0) == 0
            && SpeechEnvelope.FromSamples(quiet, frequency, 1).MouthAt(0.28f) == 0,
            "empty clips and low-level noise remain closed");
        for (int i = 0; i < 300; i++)
        {
            float seconds = i / 100f;
            Require(envelope.MouthAt(seconds) >= 0 && envelope.MouthAt(seconds) <= 2
                && envelope.MouthAt(seconds) == stereoEnvelope.MouthAt(seconds),
                "audio-clock frames remain valid and stereo phase cannot erase speech");
        }

        // Per-dialogue bindings must survive the first-line voice alias and cannot
        // inherit a later reaction's mood or turn stage directions into speech.
        var mod = new XiaomanMod();
        var bind = typeof(XiaomanMod).GetMethod("BindVoice", BindingFlags.NonPublic | BindingFlags.Instance);
        var read = typeof(XiaomanMod).GetMethod("TryLinePerformance", BindingFlags.NonPublic | BindingFlags.Instance);
        var first = new Dialogue(); var reaction = new Dialogue(); var knock = new Dialogue();
        bind.Invoke(mod, new object[] { first, "xm_01_01", "approved subtitle", Mood.Shy, true });
        bind.Invoke(mod, new object[] { reaction, "xm_act_markup_reject_01", "approved reaction", Mood.Worried, true });
        bind.Invoke(mod, new object[] { first, "intro_1", "approved subtitle", null, true });
        object[] args = { first, Mood.Neutral, false };
        Require((bool)read.Invoke(mod, args) && (Mood)args[1] == Mood.Shy && (bool)args[2],
            "first-line voice alias retains its own emotion despite later reaction bindings");
        args = new object[] { reaction, Mood.Neutral, false };
        Require((bool)read.Invoke(mod, args) && (Mood)args[1] == Mood.Worried, "reaction mood belongs to that exact dialogue");
        bind.Invoke(mod, new object[] { knock, "xm_act_shutter_knock_01", "knock", Mood.Neutral, false });
        args = new object[] { knock, Mood.Neutral, true };
        Require((bool)read.Invoke(mod, args) && !(bool)args[2], "bound stage sound is explicitly non-speaking");
        Console.WriteLine("PASS: voice-led lips, speech pauses, subtitle fallback, skip/interrupt/hidden cleanup, per-line emotions and voice aliases.");
    }
    private static void CheckNotebook()
    {
        for (int n = 1; n <= 14; n++)
        {
            string id = XiaomanMod.Id + ".story_note_" + n.ToString("00");
            Require(NotebookRules.CanStore(id, true, true, false), "existing story letters remain valid notebook contents");
            Require(!NotebookRules.CanStore(id, false, true, false)
                && !NotebookRules.CanStore(id, true, false, false)
                && !NotebookRules.CanStore(id, true, true, true), "ownership and non-container restrictions prevent unsafe nesting");
        }
        Require(NotebookRules.IsLetter(XiaomanMod.Id + ".thank_you_note"), "legacy thank-you note remains storable");
        foreach (string invalid in new[] { null, "", "tutorial_page", "tutorial_book", "water", NotebookRules.ItemId,
            XiaomanMod.Id + ".tape.title", XiaomanMod.Id + ".cocktail.sunset_blue", "other.mod.story_note_01",
            XiaomanMod.Id + ".story_note_00", XiaomanMod.Id + ".story_note_15", XiaomanMod.Id + ".story_note_1",
            XiaomanMod.Id + ".story_note_01_extra", XiaomanMod.Id + ".thank_you_note_extra" })
            Require(!NotebookRules.CanStore(invalid, true, true, false), "foreign items, drinks, tapes and notebooks cannot enter the letterbook");
        Require(!NotebookRules.ShouldGrant(false, false, false, 1, 1)
            && NotebookRules.ShouldGrant(false, false, false, 2, 1), "notebook accompanies the next-day letter, not the initial conversation");
        Require(NotebookRules.ShouldGrant(false, false, true, 50, -1), "old saves with only a legacy letter qualify for one notebook");
        Require(NotebookRules.ShouldGrant(false, false, false, 100, 1), "completed and delayed old story saves still qualify");
        Require(!NotebookRules.ShouldGrant(true, false, true, 100, 1)
            && !NotebookRules.ShouldGrant(false, true, true, 100, 1), "saved receipt or existing notebook prevents another grant");
        Require(!NotebookRules.ShouldGrant(false, false, false, 100, -1)
            && !NotebookRules.ShouldGrant(false, false, false, 100, 101)
            && !NotebookRules.ShouldGrant(false, false, true, 0, -1), "no phantom grant before a valid game/visit");
        Require(ItemPresentation.OwnsItemId(NotebookRules.ItemId), "notebook uses the existing scoped tooltip presentation");
        Require(typeof(DirectoryUtils).GetMethod("CreateBookWindow", Type.EmptyTypes) != null,
            "letterbook binds to the installed game's native book container API");
        Console.WriteLine("PASS: dedicated letter whitelist, native book API, safe nesting, first-letter delivery, legacy compensation and no duplicate grants.");
    }
    private static void CheckCassetteCase()
    {
        foreach (CassetteTrack track in CassetteCollection.Tracks)
        {
            Require(CassetteCaseRules.CanStore(track.ItemId, true, true, true, false),
                "every acquired story cassette fits the case whitelist");
            Require(!CassetteCaseRules.CanStore(track.ItemId, false, true, true, false)
                && !CassetteCaseRules.CanStore(track.ItemId, true, false, true, false)
                && !CassetteCaseRules.CanStore(track.ItemId, true, true, false, false)
                && !CassetteCaseRules.CanStore(track.ItemId, true, true, true, true),
                "foreign ownership, missing native tape tag and nested containers are rejected");
        }
        Require(CassetteCaseRules.CanStore(XiaomanMod.Id + ".rain_cassette", true, true, true, false),
            "old-save rain cassette remains storable");
        foreach (string invalid in new[] { null, "", "cassette1", "water", NotebookRules.ItemId, CassetteCaseRules.ItemId,
            XiaomanMod.Id + ".story_note_01", XiaomanMod.Id + ".cocktail.sunset_blue", "other.mod.tape.title",
            XiaomanMod.Id + ".tape.title_extra", XiaomanMod.Id + ".rain_cassette_extra", XiaomanMod.Id + ".tape.unknown" })
            Require(!CassetteCaseRules.CanStore(invalid, true, true, true, false),
                "only exact Xiaoman tape IDs, never other containers or unrelated items");
        foreach (bool nativeAccepts in new[] { false, true })
        {
            Require(CassetteCaseRules.MachineStorageAccepts(CassetteCaseRules.ItemId, true, true, nativeAccepts)
                && !CassetteCaseRules.MachineStorageAccepts(CassetteCaseRules.ItemId, false, true, nativeAccepts),
                "storage adds only the owned case exception");
            foreach (bool owned in new[] { false, true })
            {
                Require(CassetteCaseRules.MachineStorageAccepts(CassetteCaseRules.ItemId, owned, false, nativeAccepts) == nativeAccepts,
                    "disabling restores native acceptance even inside another mod's retained wrapper");
                foreach (string id in new[] { null, "cassette1", CassetteCollection.Tracks[0].ItemId, NotebookRules.ItemId, "water" })
                    Require(CassetteCaseRules.MachineStorageAccepts(id, owned, true, nativeAccepts) == nativeAccepts,
                        "all other items retain the original native or other-mod storage decision");
            }
        }
        Require((CassetteCaseRules.Columns / 2) * (CassetteCaseRules.Rows / 2) >= CassetteCollection.Tracks.Length + 1,
            "case fits all fourteen native 2x2 story tapes plus the legacy cassette");
        Require(ItemPresentation.OwnsItemId(CassetteCaseRules.ItemId), "case tooltip stays in Xiaoman's existing presentation scope");
        Require(typeof(DirectoryUtils).GetMethod("CreateGridWindow", new[] { typeof(int), typeof(int) }) != null
            && typeof(MachineHelper).GetMethod("GetBodyStorage", new[] { typeof(GameItem), typeof(int) }) != null
            && typeof(GameInventory).GetProperty("mayInventoryAddItemFunc").PropertyType == typeof(Func<GameItem, GameInventory, bool>),
            "case and warehouse extension bind to the installed game's native container APIs");
        var patches = typeof(XiaomanMod).Assembly.GetTypes().Where(type => type.IsDefined(typeof(HarmonyPatch), false)).ToArray();
        Require(patches.Where(type => ((HarmonyPatch)type.GetCustomAttributes(typeof(HarmonyPatch), false).Single()).info.declaringType == typeof(MachineCassettePlayer))
            .All(type => type.Name.StartsWith("TavernDeck")), "only tavern-owned native deck adapters may patch playback");
        Console.WriteLine("PASS: cassette case capacity, exact owned-tape whitelist, legacy storage, native filter preservation and unchanged playback bindings.");
    }
    private static void CheckTavernMap()
    {
        var mod = new XiaomanMod();
        var create = typeof(XiaomanMod).GetMethod("CreateTavernMapLocation", BindingFlags.NonPublic | BindingFlags.Instance);
        var location = (DelegateAfterhourLocation)create.Invoke(mod, null);
        Require(location.Id == TavernMapRules.LocationId && location.Source == XiaomanMod.Id,
            "tavern is a uniquely named mod-owned extra location");
        Require(!location.IsDiscovered() && !location.IsAvailable(),
            "experimental location starts hidden and unavailable without constructing a store or scene");
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var capture = typeof(XiaomanMod).GetMethod("CaptureTavernSetting", flags);
        var setting = (ConfigEntry<bool>)Activator.CreateInstance(typeof(ConfigEntry<bool>), flags, null,
            new object[] { TavernRules.ExperimentalDefault }, null);
        typeof(XiaomanMod).GetField("experimentalTavern", flags).SetValue(mod, setting);
        typeof(XiaomanMod).GetField("enabled", flags).SetValue(mod, true);
        capture.Invoke(mod, null);
        typeof(XiaomanMod).GetMethod("EnsureTavernMap", flags).Invoke(mod, null);
        Require(!setting.Default && !location.IsDiscovered() && !location.IsAvailable(),
            "active mod keeps the default-off tavern hidden and skips native registration and save initialization");
        Require(typeof(XiaomanMod).GetField("tavernScenePreview", flags) == null, "the retired preview setting cannot override calendar access");
        setting.Set(true);
        Require(!location.IsDiscovered(), "restart-required opt-in does not change the current travel session");
        capture.Invoke(mod, null);
        Require((bool)typeof(XiaomanMod).GetProperty("TavernMapEnabled", flags).GetValue(mod)
            && !location.IsDiscovered(), "opt-in is captured but cannot expose a location before an active run");
        setting.Set(false);
        Require((bool)typeof(XiaomanMod).GetProperty("TavernMapEnabled", flags).GetValue(mod),
            "pending opt-out does not dismantle an active tavern session");
        capture.Invoke(mod, null);
        Require(!location.IsDiscovered() && !location.IsAvailable(), "the next disabled session hides the tavern again");
        Require(location.ImageKey == TavernMapRules.ImageKey && location.title != null,
            "renovation card displays the author's approved tavern preview while remaining unavailable");
        Require(location.onArrive != null && location.onLeave != null && location.onTick != null && location.buildPanel == null
            && location.stageObject == null && location.panelObject == null,
            "night callbacks exist; scene and panel are allocated lazily through native travel");
        Require(location.UsesAfterhourBag && !location.ShowsStoreCounters && !location.AllowsHazardousWaste && !location.CountsDumpedWaste,
            "tavern shows the native bag while keeping waste and service counters disabled");
        Require(TavernMapRules.UsesRenovationLabel(location.Id, true)
            && !TavernMapRules.UsesRenovationLabel(location.Id, false), "status label only applies while this mod is enabled");
        foreach (string other in new[] { null, "", "dumping_ground", "commissary", "dr_jackson", "night_market",
            "other.mod.tavern", XiaomanMod.Id + ".tavern_extra" })
            Require(!TavernMapRules.UsesRenovationLabel(other, true), "native and other mod cards retain their own status text");
        Require(typeof(AfterhourLocation).GetMethod("Register", new[] { typeof(AfterhourLocation) }) != null
            && typeof(ModHelper).GetMethod("RemoveAfterhourLocations", new[] { typeof(string) }) != null,
            "extra location uses the native registration and source-scoped removal APIs");
        Require(File.Exists(Path.Combine(package,"Textures","Items",TavernMapRules.PreviewKey + ".png")),
            "preview is in the installed loader's native sprite folder");
        foreach (string file in new[] { "background.png", "bar-occluder.png", "ambient.mp4" })
            Require(File.Exists(Path.Combine(package,"Scenes","Tavern",file)), "night scene texture is packaged");
        Type ambient = typeof(XiaomanMod).Assembly.GetType("XiaomanVisitor.XiaomanTavernAmbient");
        Type player = ambient.GetField("player", flags).FieldType;
        Require(player.GetMethod("Prepare") != null && player.GetMethod("Stop") != null
            && player.GetEvent("frameReady") != null && player.GetEvent("errorReceived") != null,
            "installed Unity video API supports first-frame activation, fallback and explicit shutdown");
        Type typewriter = typeof(TextBubble).GetField("typewriter").FieldType;
        foreach (string callback in new[] { "onTextShowed", "onTypewriterStart", "onTextDisappeared", "onCharacterVisible", "onMessage" })
            Require(typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(typewriter.GetField(callback).FieldType),
                "cloned native dialogue callback can be detached from shop flow: " + callback);
        Require(typeof(DialogBubbleClickAdvance).GetMethod("Attach", new[] { typeof(UnityEngine.GameObject), typeof(Action) }) != null
            && typeof(DialogBubbleWidth).GetField("maxWidth").FieldType == typeof(float)
            && typeof(DialogUIManager).GetMethod("ScaleByDialogSpeed", new[] { typeof(float) }) != null,
            "native bubble click, wrapping and timing APIs match the installed game");
        Console.WriteLine("PASS: default-off experimental tavern, restart-scoped opt-in/out, native registration, lazy scene and source-safe removal.");
    }

    private static void CheckTavernProgress()
    {
        WeekTwoChecks.Run(package,Require);
        WeekThreeChecks.Run(package,Require);
        foreach (int height in new[] { 540,720,900,1080,1440,2160 }) {
            double width = height * 16.0 / 9;
            double scale = TavernRules.FitScale(width,height);
            Require(scale > 0 && scale*5013 <= width+1e-8 && scale*2823 <= height+1e-8, "one contain transform fits the active room without its codec padding");
        }
        Type layout = typeof(XiaomanMod).Assembly.GetType("XiaomanVisitor.TavernSceneLayout");
        Func<string,int> dimension = key => (int)layout.GetField(key, BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
        Require(dimension("ActorWidth")*688 == dimension("ActorHeight")*384
            && dimension("ActorWidth")==336 && dimension("ActorHeight")==602,
            "tavern portrait shrinks uniformly by one eighth without changing the shared atlas aspect");
        Require(dimension("ActorY")+dimension("ActorHeight")==1692
            && dimension("ActorX")*2+dimension("ActorWidth")==5012
            && dimension("BarX")==dimension("ActorX") && dimension("BarWidth")==dimension("ActorWidth"),
            "smaller tavern portrait keeps its waist and center while counter occlusion follows the same layout");
        Require(TavernRules.IdleLift(0,true)==0 && Math.Abs(TavernRules.IdleLift(4,true))<.0001f
            && Math.Abs(TavernRules.IdleLift(2,true)-8)<.0001f,
            "idle breathing starts at rest, rises smoothly and returns over four seconds");
        Require(TavernRules.IdleLift(2,false)==0 && TavernRules.IdleLift(-1,true)==0
            && TavernRules.IdleLift(float.NaN,true)==0 && TavernRules.IdleLift(float.PositiveInfinity,true)==0,
            "disabled or invalid idle clocks keep the actor at rest");
        Require(TavernRules.IdleLift(2,true)<dimension("ActorY")+dimension("ActorHeight")-dimension("BarY"),
            "maximum breath lift still keeps the waist behind the fixed counter");
        var light = TavernActorLight.Parse(File.ReadAllText(Path.Combine(package,"Scenes","Tavern","actor-light.json")));
        Require(light.Nodes==15 && light.Frames==100 && light.Fps==20 && light.RGB.Length==4500 && light.GainScale==1.75f,
            "tiny RGB light table matches the twenty-fps room, without portrait copies or alpha data");
        Require(light.FrameAt(-1)==0 && light.FrameAt(0)==0 && light.FrameAt(100)==0 && light.FrameAt(199)==99,
            "still fallback and native looped frame indices select the same light phase");
        int face = 2*light.X.Length+1;
        bool protectedFace = true;
        int head = 4*light.X.Length+1, headMin=255, headMax=0;
        for (int frame=0;frame<light.Frames;frame++) {
            int offset=(frame*light.Nodes+face)*3;
            for (int channel=0;channel<3;channel++) {
                float gain=light.RGB[offset+channel]*light.GainScale/255;
                protectedFace &= gain>=.9f && gain<=1.15f;
            }
            int red=light.RGB[(frame*light.Nodes+head)*3];
            headMin=Math.Min(headMin,red); headMax=Math.Max(headMax,red);
        }
        Require(protectedFace, "face center remains lightly shaded and retains its original palette throughout the loop");
        Require(headMax*light.GainScale/255>1.25f && (headMax-headMin)*light.GainScale/255>.25f,
            "warm head lighting can visibly brighten above source colors and varies with the room");
        bool rejectedLight = false;
        try { TavernActorLight.Parse("{\"schema\":2,\"gain_scale\":1.75,\"frame_count\":100,\"fps\":20,\"x\":[0,1],\"y\":[0,1],\"rgb\":\"\"}"); }
        catch (FormatException) { rejectedLight = true; }
        Require(rejectedLight, "an incomplete light resource is rejected so presentation can fall back to original colors");
        foreach (var scale in new[] {0f,2.1f,float.NaN}) {
            bool rejectedScale=false;
            try { TavernActorLight.Parse(JsonConvert.SerializeObject(new {schema=2,gain_scale=scale,frame_count=1,
                fps=20,x=new[]{0,1},y=new[]{0,1},rgb=Convert.ToBase64String(new byte[12])})); }
            catch (FormatException) { rejectedScale=true; }
            Require(rejectedScale,"invalid tint headroom is rejected before material setup");
        }
        Require(TavernRules.FitScale(0,1080) == 0 && TavernRules.FitScale(double.NaN,1080) == 0, "unready viewports do not generate invalid transforms");
        for (int mood=0;mood<6;mood++) for (int mouth=0;mouth<3;mouth++)
            Require(TavernRules.FrameIndex((Mood)mood,mouth) == mood*3+mouth, "all eighteen night portrait states retain approved mapping");
        Console.WriteLine("PASS: week-two opening, onsite drink recovery and preserved scene geometry.");
    }
    private static void CheckPortraitDetail()
    {
        foreach (int height in new[] { -1, 0 })
            Require(!PortraitDetailRules.WantsHighDetail(height,8192), "unready viewports retain the standard fallback");
        foreach (int height in new[] { 540, 720, 768, 900, 1080, 1081, 1440, 1600, 2160, 4320 })
        {
            Require(PortraitDetailRules.WantsHighDetail(height,4096), "ready viewport uses detailed native Sprite frames");
            Require(!PortraitDetailRules.WantsHighDetail(height,2048) && !PortraitDetailRules.WantsHighDetail(height,0),
                "unsupported GPU limits retain the standard portrait fallback");
        }
        foreach (int ppu in new[] { 60, 100, 125, 180 })
        {
            float highPPU=ppu*PortraitDetailRules.HighScale;
            Require(PortraitDetailRules.FrameWidth/(float)ppu == PortraitDetailRules.FrameWidth*PortraitDetailRules.HighScale/highPPU
                && PortraitDetailRules.FrameHeight/(float)ppu == PortraitDetailRules.FrameHeight*PortraitDetailRules.HighScale/highPPU,
                "resolution switch cannot enlarge the character or move its pivot");
        }
        Require(File.Exists(Path.Combine(package,PortraitDetailRules.HighFile)), "original-detail pack is installed for lazy loading");
        Require(File.Exists(Path.Combine(package,PortraitDetailRules.PreferredFile))
            && PortraitDetailRules.PreferredFile != PortraitDetailRules.HighFile,
            "shared accepted artwork and the separately packaged nearest fallback both exist");
        Require(typeof(LetterboxManager).GetProperty("GameAreaHeight") != null,
            "detail selection uses the installed game's actual letterboxed viewport height");
        Console.WriteLine("PASS: detailed frames across all ready viewport sizes, GPU fallback and identical portrait world size.");
    }
    private static void CheckShopPortrait()
    {
        Require(NativePortraitRules.MayUseDetail(true,false,true,true,true,true), "active Xiaoman uses detail");
        Require(NativePortraitRules.MayUseDetail(false,true,true,true,true,true), "departing portrait remains detailed after current-client cleanup");
        Require(!NativePortraitRules.MayUseDetail(false,false,true,true,true,true), "other customers retain native sampling");
        Require(!NativePortraitRules.MayUseDetail(true,true,false,true,true,true)
            && !NativePortraitRules.MayUseDetail(true,true,true,false,true,true)
            && !NativePortraitRules.MayUseDetail(true,true,true,true,false,true)
            && !NativePortraitRules.MayUseDetail(true,true,true,true,true,false), "outside, hidden, special camera and missing detail restore ownership");
        foreach (bool pixelPerfect in new[] { false,true }) foreach (bool force in new[] { false,true }) {
            var lease = new NativeSamplingLease(); bool restored, target;
            Require(lease.Begin(pixelPerfect,force) && lease.Active, "capture native sampling values once");
            Require(!lease.Begin(false,false), "later frames cannot overwrite the original native settings");
            Require(lease.End(out restored,out target) && restored == pixelPerfect && target == force && !lease.Active,
                "departure/disable restores original native state exactly");
            Require(!lease.End(out restored,out target), "cleanup is idempotent");
            lease.Begin(pixelPerfect,force); lease.UpdatePreference(!pixelPerfect);
            Require(lease.End(out restored,out target) && restored != pixelPerfect && target == force,
                "a native display preference changed during the visit is respected on departure");
        }
        // Exercise the installed native camera's managed target calculation.
        // No Unity native object is created and no graphical playtest is claimed.
        var pixels = (PixelPerfectCamera)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PixelPerfectCamera));
        pixels.assetsPPU=100; pixels.refResolutionX=960; pixels.refResolutionY=540;
        pixels.upscaleRT=true; pixels.pixelSnapping=true; pixels.cropFrameX=false; pixels.cropFrameY=false; pixels.stretchFill=false;
        Type native = typeof(PixelPerfectCamera).Assembly.GetType("UnityEngine.U2D.PixelPerfectCameraInternal",true);
        object internals = Activator.CreateInstance(native,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,
            null,new object[] { pixels },null);
        var calculate = native.GetMethod("CalculateCameraProperties",BindingFlags.Instance|BindingFlags.NonPublic);
        var fields = BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        foreach (int multiplier in new[] { 2,4 }) {
            calculate.Invoke(internals,new object[] { 960*multiplier,540*multiplier });
            Require((bool)native.GetField("useOffscreenRT",fields).GetValue(internals)
                && (int)native.GetField("offscreenRTWidth",fields).GetValue(internals)==960
                && (int)native.GetField("offscreenRTHeight",fields).GetValue(internals)==540,
                "installed native camera reduces 1080p and 4K to a 960x540 intermediate when enabled");
            Require(Math.Abs((float)native.GetField("orthoSize",fields).GetValue(internals)-2.7)<.00001,
                "native framing is 2.7 before the full-resolution rendering mode");
        }
        Console.WriteLine("PASS: native movement-independent scope, snapshot/restore, display changes and installed-camera downsampling calculation.");
    }

    private static void CheckPaper()
    {
        string inkPath;
        foreach (string locale in new[] { "zh-CN", "ja-JP", "en-US" })
            for (int number = 1; number <= 14; number++)
            {
                string id = "story_note_" + number.ToString("00");
                Require(LetterInkRules.TryResolve(XiaomanMod.Id, id, locale, out inkPath)
                    && inkPath == "Letters/" + locale.Substring(0, 2) + "/" + id + ".png"
                    && File.Exists(Path.Combine(package, inkPath)), "localized ink exists for each native story letter");
            }
        Require(LetterInkRules.TryResolve(XiaomanMod.Id, "thank_you_note", "zh_CN", out inkPath)
            && inkPath == "Letters/zh/story_note_01.png", "legacy paper uses the first letter's handwriting");
        Require(!LetterInkRules.TryResolve("other.mod", "story_note_01", "zh", out inkPath)
            && !LetterInkRules.TryResolve(null, "story_note_01", "zh", out inkPath), "other mod and native papers are isolated");
        foreach (string invalid in new[] { null, "", "story_note_00", "story_note_15", "story_note_1", "story_note_001",
            "story_note_+1", "story_note_01/../02", "../story_note_01", "story_note_01.png", "thank_you_note/" })
            Require(!LetterInkRules.TryResolve(XiaomanMod.Id, invalid, "en", out inkPath), "reject malformed or unrelated paper IDs");
        Require(LetterInkRules.Language("en_GB") == "en" && LetterInkRules.Language("ja-JP") == "ja"
            && LetterInkRules.Language("zh-Hans") == "zh" && LetterInkRules.Language("de-DE") == "en"
            && LetterInkRules.Language(null) == "en", "language variants and safe supported fallback");
        var note = ModHelper.ParsePaperBook(File.ReadAllText(Path.Combine(package, "Books", "thank_you_note.json")));
        Require(note != null && note.id == "thank_you_note" && note.pages.Count == 1 && note.item != null
            && note.item.id == "sansm.xiaoman.visitor.thank_you_note", "native note/item binding");
        foreach (StoryEpisode episode in StoryCatalog.Episodes)
        {
            var letter = ModHelper.ParsePaperBook(File.ReadAllText(Path.Combine(package, "Books", episode.NoteId + ".json")));
            Require(letter != null && letter.pages.Count == (episode.Index==4?2:1) && letter.id == episode.NoteId
                && letter.item.id == XiaomanMod.Id + "." + episode.NoteId, "native story letter/item binding");
        }
    }
}
