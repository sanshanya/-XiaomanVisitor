using System;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private long phoneCallSequence;
        // Native end-action timers may outlive their bubble. Bind business
        // consequences to the visitor/call and save that created them.
        private Action VisitorAction(StoreClient owner,Action action)
        {
            string run=WorkRunIdentity;int day=StoreStation.Instance.dayCounter;
            return () => {
                if (enabled && PlayerStore.HasInstance && StoreStation.Instance!=null
                    && MayFinishCallback(owner,PlayerStore.Instance.currentClientInstance?.GetClientBlueprint(),
                        run,WorkRunIdentity,day,StoreStation.Instance.dayCounter)) action();
            };
        }
        private Action PhoneAction(StorePhoneClient owner,long call,Action action)
        {
            string run=WorkRunIdentity;int day=StoreStation.Instance.dayCounter;
            return () => {
                if (enabled && PlayerStore.HasInstance && StoreStation.Instance!=null && PhoneUIManager.Instance!=null
                    && MayFinishCallback(owner,PhoneUIManager.Instance.currentPhoneClient,
                        run,WorkRunIdentity,day,StoreStation.Instance.dayCounter,call,phoneCallSequence)) action();
            };
        }
        internal static bool MayFinishCallback(object owner,object current,string openedRun,string currentRun,int openedDay,int currentDay,
            long openedCall=0,long currentCall=0)
        {
            return owner!=null && ReferenceEquals(owner,current) && !string.IsNullOrEmpty(openedRun)
                && openedRun==currentRun && openedDay==currentDay && openedCall==currentCall;
        }
    }
}
