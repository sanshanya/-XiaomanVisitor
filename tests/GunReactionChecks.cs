using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using XiaomanVisitor;

internal static class GunReactionChecks
{
    internal static void Run(string package, Action<bool, string> check)
    {
        var reaction = new GunReaction();
        reaction.Raise(10);
        check(reaction.Row(10.2f) == 0 && reaction.Row(10.5f) == 1
            && reaction.Phase == GunReactionPhase.Aiming, "fright starts sharply and settles into sustained concern");
        check(reaction.Row(1000) == 1, "finishing the line cannot clear the aimed-at expression");
        reaction.Recover(); reaction.Raise(1001);
        check(reaction.Phase == GunReactionPhase.Startled && reaction.ConsumePreservedPause(),
            "a raise during recovery keeps the original native paused conversation");
        reaction.Raise(1001.1f);
        check(!reaction.ConsumePreservedPause(), "the later unholster factory cannot pause the temporary reaction again");
        reaction.Recover(); reaction.Resumed(1002);
        check(reaction.Row(1002.5f) == 1 && reaction.Row(1003.2f) == -1 && !reaction.Active,
            "resuming the native conversation leaves brief concern and then releases presentation");
        reaction.Raise(2000); reaction.Reset();
        check(!reaction.Active && reaction.Row(2000) == -1, "departure and reload discard all transient gun state");
        check(GunReaction.Protects(true, XiaomanMod.ClientId)
            && GunReaction.Protects(true, XiaomanMod.ClientId+".work_thanks")
            && !GunReaction.Protects(false, XiaomanMod.ClientId) && !GunReaction.Protects(true, "native_customer")
            && !GunReaction.Protects(true, XiaomanMod.ClientId+".another_mod")
            && !GunReaction.Protects(true, null), "test shot protection is restricted to an enabled Xiaoman target");

        var pulse = new CharacterMouthPulse(); pulse.Begin(20); pulse.Observe('可', 20.1f);
        check(pulse.Mouth(true, 20.14f) > 0, "native visible letters animate the mouth");
        pulse.Observe('…', 20.15f);
        check(pulse.Mouth(true, 20.16f) == 0, "native punctuation pauses close the mouth");
        pulse.Observe('下', 20.2f);
        check(pulse.Mouth(false, 20.25f) == 0 && pulse.Mouth(true, 20.5f) == 0,
            "skipping, disabling typewriter and stale character pulses never keep the mouth moving");

        var mod = new XiaomanMod();
        var client = new StoreClient { arrestable = false, nonShootable = true };
        typeof(XiaomanMod).GetMethod("ConfigureGunReaction", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(mod, new object[] { client });
        check(!client.arrestable && client.forceShootable && !client.nonShootable && client.holsterResumesDialogue
            && client.unholsterDialogue != null, "native draw permission does not enable arrest and retains native pause/resume");
        foreach (var target in new[] {
            Tuple.Create(typeof(StoreUIManager), "EnterShootingMode"), Tuple.Create(typeof(StoreUIManager), "CanShoot"),
            Tuple.Create(typeof(StoreUIManager), "Shoot"), Tuple.Create(typeof(DialogUIManager), "PauseConversation"),
            Tuple.Create(typeof(DialogUIManager), "ResumeConversation"), Tuple.Create(typeof(DialogUIManager), "DisplayClientText") })
            check(AccessTools.Method(target.Item1, target.Item2) != null, "installed native gun/dialogue method exists: " + target.Item2);
        check(File.Exists(Path.Combine(package, "Portraits/xiaoman_gun_atlas.png")), "the six-frame reaction atlas ships with the build");
        // With no active mod, neither guard suppresses an unrelated native action.
        bool result = true;
        object[] args = { result };
        check((bool)typeof(XiaomanGunCanShootPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args)
            && (bool)args[0] && (bool)typeof(XiaomanGunShootPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null),
            "inactive hooks preserve the native shooting decision and action");
        Console.WriteLine("PASS: scoped aim-only guard, native draw/dialogue API bindings, transient recovery and character-driven mouth timing. Gameplay remains author-tested.");
    }
}
