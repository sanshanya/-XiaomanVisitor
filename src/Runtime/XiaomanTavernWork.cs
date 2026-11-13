using System;
using System.Linq;
using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private bool workRunning, wageBusy;
        private int workDay, workSounds;
        private float workElapsed;
        private string[] workOrderIds;
        private string WorkRunIdentity
        { get { return string.IsNullOrEmpty(PlayerStore.Instance.runID) ? "slot:"+PlayerStore.Instance.saveSlotId+":"+PlayerStore.Instance.runNumber : PlayerStore.Instance.runID; } }
        private TavernWorkProgress ReadWork()
        { return TavernWorkProgress.Restore(ModHelper.GetModData(Id,TavernWorkProgress.SaveKey,""),WorkRunIdentity,StoreStation.Instance.dayCounter); }
        private void SaveWork(TavernWorkProgress p)
        { ModHelper.SetModData(Id,TavernWorkProgress.SaveKey,p.Data); }
        private void EnablePaidWork()
        {
            ModHook.OnShutterClosedLate+=GenerateNightDemand;
            ModHook.OnAfterhourArriveEarly+=OnWorkNightArrive;
            ModHook.OnGameLoadedLate+=OnWorkLoaded;
            ModHook.OnReturningStoreLate+=RecoverWorkWage;
            ModHook.OnGoingSleepEarly+=RecoverWorkWage;
        }
        private void DisablePaidWork()
        {
            CancelTavernWork();
            ModHook.OnShutterClosedLate-=GenerateNightDemand;
            ModHook.OnAfterhourArriveEarly-=OnWorkNightArrive;
            ModHook.OnGameLoadedLate-=OnWorkLoaded;
            ModHook.OnReturningStoreLate-=RecoverWorkWage;
            ModHook.OnGoingSleepEarly-=RecoverWorkWage;
        }
        private void OnWorkNightArrive(string location) { GenerateNightDemand(); }
        private void OnWorkLoaded()
        {
            CancelTavernWork(); RecoverWorkWage();
            if (PlayerStore.HasInstance && PlayerStore.Instance.storeState==PlayerStore.StoreState.CLOSED) GenerateNightDemand();
            FreezeThirdWorkLetter();
        }
        private void GenerateNightDemand()
        {
            if (!TavernMapEnabled || !PlayerStore.HasInstance || !TavernTrading
                || StoreStation.Instance.dayCounter<TavernWorkConfig.UnlockDay) return;
            var p=ReadWork(); int day=StoreStation.Instance.dayCounter;
            if (p.EnsureDemand(day,TavernWorkConfig.DemandCount(day))) SaveWork(p);
        }
        private void RequestTavernWork()
        {
            if (!TavernMayAct || !TavernTrading || TavernDialogueBusy || wageBusy) return;
            var p=ReadWork(); int day=StoreStation.Instance.dayCounter;
            CloseTavernMenu();
            if (EconomyAvailable && ReadEconomy().ProjectTonight(day)) { TavernNotice("ui_eco_project_wait");return; }
            if (p.RecoveryBlocked || p.Pending!=null) { TavernNotice("ui_tavern_wage_pending"); return; }
            if (p.WorkedOn(day)) { QueueTavernSpeech(new[]{"xm_tavern_w03_work_done_today"}); return; }
            if (!p.MayWork(day)) { QueueTavernSpeech(new[]{"xm_tavern_w03_work_no_orders"}); return; }
            if (p.WorkIntroSeen) { ShowTavernMenu("work"); return; }
            bool callback=ReadTavern().OpeningReceptionHelped && !p.OpeningCallbackSeen;
            var keys=new System.Collections.Generic.List<string>();
            if (callback) keys.Add("xm_tavern_w03_work_w02_callback");
            keys.Add("xm_tavern_w03_work_offer_01");keys.Add("xm_tavern_w03_work_offer_02");
            QueueTavernSpeech(keys,() => {
                var progress=ReadWork(); progress.WorkIntroSeen=true;
                if (callback) progress.OpeningCallbackSeen=true;
                SaveWork(progress); ShowTavernMenu("work");
            });
        }
        private void StartTavernWork()
        {
            if (!TavernMayAct || !TavernTrading || TavernDialogueBusy || wageBusy) return;
            var p=ReadWork(); workDay=StoreStation.Instance.dayCounter;
            if (EconomyAvailable && ReadEconomy().ProjectTonight(workDay)) return;
            if (!p.MayWork(workDay)) { RequestTavernWork(); return; }
            workOrderIds=p.WaitingIds(workDay); workElapsed=0;workSounds=0;
            CloseTavernMenu();
            QueueTavernSpeech(new[]{"xm_tavern_w03_work_start"},() => {
                if (!TavernMayAct || !TavernTrading || StoreStation.Instance.dayCounter!=workDay || !ReadWork().MayWork(workDay)) return;
                workRunning=true; tavernStage.SetWorkProgress(0,Text("ui_tavern_work_phase_1"));
            });
        }
        private void TickTavernWork()
        {
            if (!workRunning) return;
            if (!TavernMayAct || StoreStation.Instance.dayCounter!=workDay || tavernStage==null || !tavernStage.gameObject.activeInHierarchy) {
                CancelTavernWork(); return;
            }
            if (Time.timeScale<=0 || AudioListener.pause) return;
            workElapsed+=Time.unscaledDeltaTime;
            float value=Mathf.Clamp01(workElapsed/TavernWorkConfig.DurationSeconds);
            tavernStage.SetWorkProgress(value,Text(value<.33f?"ui_tavern_work_phase_1":value<.75f?"ui_tavern_work_phase_2":"ui_tavern_work_phase_3"));
            if (workSounds==0 && value>=.15f) { PlayWorkGlass();workSounds=1; }
            if (workSounds==1 && value>=.65f) { PlayWorkGlass();workSounds=2; }
            if (value<1) return;
            workRunning=false;tavernStage.HideWorkProgress();
            CommitTavernWork();
        }
        private void CancelTavernWork()
        { workRunning=false;workElapsed=0;workOrderIds=null;tavernStage?.HideWorkProgress(); }
        private static void PlayWorkGlass()
        {
            if (!AudioManager.Exists || !AudioManager.Instance.CanPlay("glass_drop")) return;
            var audio=AudioManager.Instance;
            // Use the native SFX mix at reduced gain, without changing its shared volume/clip.
            audio.audioSource3.PlayOneShot(audio.audioDirectory.GetAudioClip("glass_drop"),.35f);
        }
        private void CommitTavernWork()
        {
            if (!TavernMayAct || !TavernTrading || wageBusy) return;
            wageBusy=true;
            try {
                var p=ReadWork(); bool first=!p.HasWorked;
                var shift=p.PrepareShift(workDay,PlayerStore.Instance.GetCash(),workOrderIds);
                if (shift==null) { QueueTavernSpeech(new[]{"xm_tavern_w03_work_no_orders"});return; }
                SaveWork(p);
                ModHelper.AddCredit(shift.Wage,false);
                if (p.Recover(workDay,PlayerStore.Instance.GetCash())!=WageRecovery.Finish || !p.Complete(shift))
                    throw new InvalidOperationException("Work payment or source orders no longer match");
                SaveWork(p);
                QueueTavernSpeech(first ? new[]{"xm_tavern_w03_work_done_01","xm_tavern_w03_work_done_02"}
                    : new[]{"xm_tavern_w03_work_done_02"});
            }
            catch (Exception e) { Log.Warning("Work payment held for recovery: "+e.Message);TavernNotice("ui_tavern_wage_pending"); }
            finally { wageBusy=false;workOrderIds=null; }
        }
        private void RecoverWorkWage()
        {
            if (!TavernMapEnabled || !PlayerStore.HasInstance || wageBusy) return;
            var p=ReadWork();var shift=p.Pending;
            if (shift==null) return;
            wageBusy=true;
            try {
                var action=p.Recover(StoreStation.Instance.dayCounter,PlayerStore.Instance.GetCash());
                if (action==WageRecovery.Cancel) shift.Status="cancelled";
                else if (action==WageRecovery.Finish) {
                    if (!p.Complete(shift)) shift.Status="uncertain";
                }
                else if (action==WageRecovery.Hold) shift.Status="uncertain";
                SaveWork(p);
            }
            catch (Exception e) { Log.Warning("Work receipt remains pending: "+e.Message); }
            finally { wageBusy=false; }
        }
    }
}
