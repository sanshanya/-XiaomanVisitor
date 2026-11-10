using System;
using UnityEngine;
using UnityEngine.UI;

namespace XiaomanVisitor
{
    internal sealed class TavernChoice
    {
        internal readonly string Label;
        internal readonly Action Select;
        internal TavernChoice(string label,Action select) { Label=label;Select=select; }
    }

    // Only the native choice artwork and hover behaviour are cloned. No native
    // conversation, selection index or transaction callback belongs to this menu.
    internal sealed class XiaomanTavernChoices
    {
        internal readonly RectTransform Root;
        private readonly DialogChoiceBubble[] views=new DialogChoiceBubble[4];
        private readonly RectTransform[] rects=new RectTransform[4];
        private readonly DialogBubbleScaler scaler=new DialogBubbleScaler();
        private TavernChoice[] options=new TavernChoice[0];
        private bool dirty=true;
        private Vector2 previousSize,previousActor;
        private float previousBottom,previousScale;
        internal bool Visible { get { return Root.gameObject.activeSelf; } }
        internal XiaomanTavernChoices(RectTransform room)
        {
            var templates=DialogUIManager.Instance.dialogChoiceBubbles;
            if (templates==null || templates.Count==0 || templates[0]==null)
                throw new InvalidOperationException("Native dialogue choices are not ready");
            Root=new GameObject("Xiaoman.NativeChoices",typeof(RectTransform)).GetComponent<RectTransform>();
            Root.SetParent(room,false);Root.anchorMin=Root.anchorMax=Root.pivot=new Vector2(0,1);
            Root.gameObject.SetActive(false);
            for (int i=0;i<views.Length;i++) {
                var view=UnityEngine.Object.Instantiate(templates[0],Root,false);
                views[i]=view;view.SetInteractable(true);view.enabled=false;
                var rect=(RectTransform)view.transform;rects[i]=rect;
                rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
                rect.localScale=Vector3.one;scaler.Register(rect);
                foreach (StoreUI marker in view.GetComponentsInChildren<StoreUI>(true)) {
                    marker.identifier=XiaomanMod.Id+".tavern.choices";marker.isPaperUI=false;marker.zoomable=false;
                    marker.enabled=false;UnityEngine.Object.Destroy(marker);
                }
                foreach (ContentSizeFitter fit in view.GetComponents<ContentSizeFitter>()) fit.enabled=false;
                foreach (DialogBubbleWidth width in view.GetComponents<DialogBubbleWidth>()) width.enabled=false;
                foreach (LayoutGroup layout in view.GetComponents<LayoutGroup>()) layout.enabled=false;
                var textRect=view.TMP.rectTransform;
                textRect.anchorMin=Vector2.zero;textRect.anchorMax=Vector2.one;
                textRect.offsetMin=new Vector2(12,5);textRect.offsetMax=new Vector2(-12,-5);
                view.TMP.fontSize=18;view.TMP.enableAutoSizing=false;
                view.TMP.alignment=TMPro.TextAlignmentOptions.MidlineLeft;
                foreach (Button button in view.GetComponentsInChildren<Button>(true)) {
                    XiaomanTavernBubble.Clear(button.onClick);
                    button.navigation=new Navigation { mode=Navigation.Mode.None };
                }
                Button select=view.GetComponent<Button>() ?? view.GetComponentInChildren<Button>(true);
                if (select==null) throw new InvalidOperationException("Native choice has no button");
                int slot=i;
                select.onClick.AddListener(() => { if (slot<options.Length) options[slot].Select?.Invoke(); });
            }
        }
        internal void Show(TavernChoice[] choices)
        {
            if (choices==null || choices.Length==0 || choices.Length>4) throw new ArgumentException("One to four choices required");
            options=choices;
            dirty=true;
            for (int i=0;i<views.Length;i++) {
                views[i].gameObject.SetActive(i<choices.Length);
                if (i>=choices.Length) continue;
                views[i].TMP.font=DialogUIManager.Instance.dialogChoiceBubbles[0].TMP.font;
                views[i].TMP.text=choices[i].Label;
                views[i].TMP.enableWordWrapping=true;
            }
            Root.gameObject.SetActive(true);
        }
        internal void Tick(RectTransform actor,Vector2 actorRest,float bottom)
        {
            if (!Visible) return;
            Rect room=((RectTransform)Root.parent).rect;
            float factor=EmporiumOption.DialogBubbleScale;
            if (!dirty && previousSize==room.size && previousActor==actor.rect.size
                && Mathf.Approximately(previousBottom,bottom) && Mathf.Approximately(previousScale,factor)) return;
            dirty=false;previousSize=room.size;previousActor=actor.rect.size;previousBottom=bottom;previousScale=factor;
            float unit=room.height/1080f;
            scaler.Apply(unit*factor);
            float y=0,w=0;
            for (int i=0;i<options.Length;i++) {
                var rect=rects[i];var text=views[i].TMP;
                float scale=Mathf.Max(.01f,rect.localScale.x);
                float max=Mathf.Max(100,Mathf.Min(280*unit,room.width*.28f)/scale);
                float width=Mathf.Clamp(text.GetPreferredValues(options[i].Label).x+24,76,max);
                float height=Mathf.Max(26,text.GetPreferredValues(options[i].Label,width-24,float.PositiveInfinity).y+12);
                rect.sizeDelta=new Vector2(width,height);rect.anchoredPosition=new Vector2(0,-y);
                w=Mathf.Max(w,width*scale);y+=height*scale+6*unit;
            }
            float heightTotal=Mathf.Max(0,y-6*unit);
            Root.sizeDelta=new Vector2(w,heightTotal);
            float x=Mathf.Min(actorRest.x+actor.rect.width+12,room.width-w-12);
            float top=Mathf.Min(-actorRest.y+actor.rect.height*.6f,bottom-heightTotal-8);
            Root.anchoredPosition=new Vector2(Mathf.Max(8,x),-Mathf.Max(8,top));
        }
        internal void Hide() { Root.gameObject.SetActive(false);options=new TavernChoice[0]; }
    }
}
