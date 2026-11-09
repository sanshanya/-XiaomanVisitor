using System;

namespace XiaomanVisitor
{
    public enum GunReactionPhase { None, Startled, Aiming, Recovering, Lingering }

    // Ephemeral presentation only: never serialized into story or trading data.
    public sealed class GunReaction
    {
        public GunReactionPhase Phase { get; private set; }
        private float since;
        private bool preservePause;
        public bool Active { get { return Phase != GunReactionPhase.None; } }
        public void Raise(float now)
        { preservePause |= Phase == GunReactionPhase.Recovering; Phase = GunReactionPhase.Startled; since = now; }
        public bool ConsumePreservedPause()
        { bool result = preservePause; preservePause = false; return result; }
        public void Recover() { Phase = GunReactionPhase.Recovering; }
        public void Resumed(float now) { Phase = GunReactionPhase.Lingering; since = now; }
        public void Reset() { Phase = GunReactionPhase.None; preservePause = false; }
        public int Row(float now)
        {
            if (Phase == GunReactionPhase.Startled && now - since >= .45f) Phase = GunReactionPhase.Aiming;
            if (Phase == GunReactionPhase.Lingering && now - since >= 1.1f) Reset();
            return !Active ? -1 : Phase == GunReactionPhase.Startled ? 0 : 1;
        }
        public static bool Protects(bool enabled, string clientId)
        { return enabled && (clientId == XiaomanMod.ClientId || clientId == XiaomanMod.ClientId+".work_thanks"); }
    }

    public sealed class CharacterMouthPulse
    {
        private bool letter;
        private float started, last;
        public void Begin(float now) { started = now; last = now; letter = false; }
        public void Observe(char character, float now)
        { letter = char.IsLetterOrDigit(character); last = now; }
        public int Mouth(bool typing, float now)
        {
            if (!typing || !letter || now < last || now - last > .18f) return 0;
            return PortraitTiming.Cadence(now - started);
        }
    }
}
