using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace XiaomanVisitor
{
    // Borrow native artwork and text styles, never CommissaryElement's purchasing
    // callbacks, stock, currency or registration lifecycle.
    internal sealed class XiaomanTavernMenuSkin
    {
        private readonly Image background,card;
        private readonly CommissaryElement product;
        private readonly TextMeshProUGUI heading;
        private readonly Button close;
        private XiaomanTavernMenuSkin(GameObject panel,CommissaryElement sample)
        {
            product=sample;
            background=panel.GetComponent<Image>() ?? panel.GetComponentsInChildren<Image>(true)
                .Where(i => !i.transform.IsChildOf(sample.transform) && i.GetComponentInParent<Button>()==null)
                .OrderByDescending(i => i.rectTransform.rect.width*i.rectTransform.rect.height).FirstOrDefault();
            card=sample.transform.Find("Panel")?.GetComponent<Image>() ?? sample.GetComponentsInChildren<Image>(true)
                .Where(i => i.GetComponentInParent<Button>()==null)
                .OrderByDescending(i => i.rectTransform.rect.width*i.rectTransform.rect.height).FirstOrDefault();
            heading=panel.transform.Find("Title")?.GetComponent<TextMeshProUGUI>()
                ?? panel.GetComponentsInChildren<TextMeshProUGUI>(true).OrderByDescending(t => t.fontSize).FirstOrDefault();
            close=panel.transform.Find("CloseButton")?.GetComponent<Button>()
                ?? panel.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name.ToLowerInvariant().Contains("close"));
        }
        internal static XiaomanTavernMenuSkin Read()
        {
            var panel=CommissaryUIManager.Instance?.panel;
            var sample=panel==null?null:panel.GetComponentInChildren<CommissaryElement>(true);
            return sample==null?null:new XiaomanTavernMenuSkin(panel,sample);
        }
        private static void ImageStyle(Image source,Image target,bool translucent=false)
        {
            if (source==null || target==null) return;
            target.sprite=source.sprite;target.type=source.type;target.preserveAspect=source.preserveAspect;
            target.fillCenter=source.fillCenter;target.pixelsPerUnitMultiplier=source.pixelsPerUnitMultiplier;
            Color color=source.color;if (translucent) color.a=Mathf.Min(color.a,.82f);
            target.color=color;
        }
        internal static void TextStyle(TextMeshProUGUI source,TextMeshProUGUI target)
        {
            if (source==null || target==null) return;
            target.font=source.font;target.color=source.color;target.fontSize=source.fontSize;
            target.fontStyle=source.fontStyle;target.enableAutoSizing=false;
            target.alignment=TextAlignmentOptions.Center;target.enableWordWrapping=true;
        }
        internal void Window(CustomUIWindow window)
        {
            Image target=window.root.GetComponentsInChildren<Image>(true)
                .Where(i => i.GetComponentInParent<Button>()==null)
                .OrderByDescending(i => i.rectTransform.rect.width*i.rectTransform.rect.height).FirstOrDefault();
            if (target!=null) {
                foreach (Image image in window.root.GetComponentsInChildren<Image>(true))
                    if (image!=target && image.GetComponentInParent<Button>()==null) image.enabled=false;
                ImageStyle(background,target,true);
            }
            TextStyle(heading,window.frame?.titleText);
            if (window.frame?.titleText!=null) window.frame.titleText.alignment=TextAlignmentOptions.MidlineLeft;
            if (close!=null && window.frame?.closeButton!=null)
                ImageStyle(close.targetGraphic as Image,window.frame.closeButton.targetGraphic as Image);
        }
        internal void Card(CustomUIElement element) { ImageStyle(card,element.image,true); }
        internal void Label(CustomUIElement element,bool name=false)
        { TextStyle(name?product.productName:product.price,element.text); }
        internal void Button(CustomUIElement element)
        {
            ImageStyle(product.buyButton.targetGraphic as Image,element.button?.targetGraphic as Image);
            TextStyle(product.buyButton.GetComponentInChildren<TextMeshProUGUI>(true),element.text);
            if (element.button!=null) element.button.colors=product.buyButton.colors;
        }
    }
}
