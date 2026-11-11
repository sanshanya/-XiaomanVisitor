using System.Collections.Generic;
using UnityEngine;

namespace XiaomanVisitor.SideStories
{
    internal sealed class BuybackMusic
    {
        private readonly ModLog log;
        private AudioSource radio;
        private AudioClip introClip, previousTrack;
        private string radioRun;
        private int radioDay;
        private float previousTime;
        private bool previousPlaying, previousPaused, previousLoop, previousTrackCallback;
        private bool retained;

        internal BuybackMusic(ModLog ownerLog) { log = ownerLog; }
        internal void RetainUntilMorning() { retained = radioRun != null && radioDay >= 1; }
        internal bool CanContinue(string run, int day) { return retained && radioRun == run && radioDay == day; }
        internal void ReleaseStage(bool leaving)
        { if (leaving || !retained) Restore(leaving); }
        internal void OnMorning(string run, int day)
        { if (radioRun != null && (radioRun != run || day > radioDay)) Restore(); }

        internal IEnumerable<object> Start(BuybackMusicSpec introMusic, string run, int day)
        {
            // Reopening the door scene after refusal must not stop, seek or replay Intro.
            if (CanContinue(run, day) && radio != null && radio.clip == introClip)
            {
                log.Log("Radio override continues from its current position after refusal.");
                yield break;
            }
            if (radio != null && (radioRun != run || radioDay != day || radio.clip != introClip)) Restore();
            var manager = AudioManager.Exists ? AudioManager.Instance : null;
            if (manager == null || manager.audioSourceMusic == null)
            {
                log.Warning("Intro unavailable: native radio source is not ready.");
                yield break;
            }
            string identifier = ModHelper.GetSoundIdentifier(BuybackTestMod.Id, introMusic.identifier);
            float until = Time.unscaledTime + 5;
            while (!manager.audioDirectory.HasAudioClip(identifier) && Time.unscaledTime < until) yield return null;
            if (!manager.audioDirectory.HasAudioClip(identifier))
            {
                log.Warning("Intro unavailable: mod music asset has not loaded.");
                yield break;
            }
            introClip = manager.audioDirectory.GetAudioClip(identifier);
            if (introClip.loadState == AudioDataLoadState.Unloaded) introClip.LoadAudioData();
            while (introClip.loadState == AudioDataLoadState.Loading && Time.unscaledTime < until) yield return null;
            if (introClip.loadState != AudioDataLoadState.Loaded)
            {
                log.Warning("Intro unavailable: audio data state=" + introClip.loadState + ".");
                yield break;
            }
            if (radio == null)
            {
                radio = manager.audioSourceMusic;
                previousTrack = radio.clip; previousTime = radio.time;
                previousPlaying = radio.isPlaying; previousLoop = radio.loop;
                previousTrackCallback = manager.audioCoroutine != null;
                previousPaused = !previousPlaying && previousTrack != null && (previousTime > 0
                    || (EmporiumEntry.Instance != null && EmporiumEntry.Instance.cassettePlayer != null
                        && EmporiumEntry.Instance.cassettePlayer.GetTagReadonly(MachineCassettePlayer.CASETTE_PLAYER_STATE_INT).GetInt() == 2));
                radioRun = run; radioDay = day;
            }
            // Inventory and cassette-player tags are never changed by this temporary override.
            AudioManager.StopAudioCoroutine();
            radio.loop = introMusic.loop;
            manager.PlayMusic(identifier);
            log.Log("Radio override: " + introMusic.title + "; from start, loop=" + radio.loop + ".");
        }

        internal void Restore(bool leaving = false)
        {
            retained = false;
            var source = radio; radio = null;
            if (ReferenceEquals(source, null)) return;
            RestoreSource(source, leaving);
        }
        private void RestoreSource(AudioSource source, bool leaving)
        {
            if (source == null || source.clip != introClip) return;
            source.Stop(); source.loop = previousLoop;
            if (leaving || !PlayerStore.HasInstance || PlayerStore.Instance.runID != radioRun)
            {
                source.clip = null;
                return;
            }
            source.clip = previousTrack;
            if (previousTrack != null && (previousPlaying || previousPaused))
            {
                source.Play();
                source.time = Mathf.Clamp(previousTime, 0, Mathf.Max(0, previousTrack.length - .001f));
                if (previousPaused) source.Pause();
            }
            if (previousPlaying && previousTrackCallback && AudioManager.Exists && AudioManager.Instance.audioSourceMusic == source)
                AudioManager.Instance.PlayAndCallback(AudioManager.OnTrackDone);
            log.Log("Native radio restored; playing=" + previousPlaying + "; paused=" + previousPaused + ".");
        }
    }
}
