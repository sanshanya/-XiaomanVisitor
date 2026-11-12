using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace XiaomanVisitor.SideStories
{
    internal sealed partial class BuybackStage
    {
        private RectTransform choiceMenu;
        private readonly DialogChoiceBubble[] choiceOptions = new DialogChoiceBubble[2];
        private int choiceDecision, choiceCount, choiceFrame;
        private bool doorQuestion;
        private float ChoiceStep { get { return doorScene ? 42 : 48; } }

        private void BuildChoiceMenu()
        {
            var templates = DialogUIManager.Instance.dialogChoiceBubbles;
            if (templates == null || templates.Count == 0 || templates[0] == null)
                throw new InvalidOperationException("Native choice template is not ready");
            choiceMenu = Child("NativeChoices", speechRoot);
            choiceMenu.anchorMin = choiceMenu.anchorMax = choiceMenu.pivot = new Vector2(0, 1);
            choiceMenu.gameObject.SetActive(false);
            for (int i = 0; i < choiceOptions.Length; i++)
            {
                var option = UnityEngine.Object.Instantiate(templates[0], choiceMenu, false);
                choiceOptions[i] = option;
                option.SetInteractable(true); option.enabled = false;
                var rect = (RectTransform)option.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.localScale = Vector3.one;
                foreach (var marker in option.GetComponentsInChildren<StoreUI>(true))
                {
                    marker.identifier = BuybackStory.Id + ".choice";
                    marker.isPaperUI = false; marker.zoomable = false;
                    marker.enabled = false; UnityEngine.Object.Destroy(marker);
                }
                foreach (var fit in option.GetComponents<ContentSizeFitter>()) fit.enabled = false;
                foreach (var width in option.GetComponents<DialogBubbleWidth>()) width.enabled = false;
                foreach (var layout in option.GetComponents<LayoutGroup>()) layout.enabled = false;
                foreach (var button in option.GetComponentsInChildren<Button>(true))
                {
                    Clear(button.onClick);
                    button.navigation = new Navigation { mode = Navigation.Mode.None };
                }
                var select = option.GetComponent<Button>() ?? option.GetComponentInChildren<Button>(true);
                if (select == null) throw new InvalidOperationException("Native choice has no button");
                int index = i;
                select.onClick.AddListener(() => SelectChoice(index));
                var text = option.TMP;
                text.enableAutoSizing = false; text.alignment = TextAlignmentOptions.MidlineLeft;
                text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.offsetMin = new Vector2(12, 5); text.rectTransform.offsetMax = new Vector2(-12, -5);
            }
        }

        private void ShowChoices(string[] options, bool isDoor)
        {
            if (options == null || options.Length != choiceOptions.Length)
                throw new InvalidOperationException("Expected two explicit choices");
            choiceFrame = Time.frameCount; choiceDecision = -1; choiceCount = options.Length; doorQuestion = isDoor;
            for (int i = 0; i < choiceOptions.Length; i++)
            {
                var text = choiceOptions[i].TMP;
                text.text = options[i]; text.fontSize = doorScene ? 20 : 28;
                ((RectTransform)choiceOptions[i].transform).sizeDelta = new Vector2(Mathf.Max(100, text.GetPreferredValues(text.text).x + 24), ChoiceStep - 6);
                choiceOptions[i].gameObject.SetActive(true);
            }
            choiceMenu.gameObject.SetActive(true);
            hint.text = isDoor ? "请选择是否开门" : "请选择回答";
        }

        private void SelectChoice(int index)
        {
            if (!choiceMenu.gameObject.activeInHierarchy || choiceDecision >= 0 || index < 0 || index >= choiceCount
                || Time.frameCount <= choiceFrame) return;
            choiceDecision = index; choiceMenu.gameObject.SetActive(false);
            hint.text = "点击 / 继续对话键　·　Esc 关闭当前演出";
            log.Log("Player choice: " + (doorQuestion ? "door" : "silence") + "; index=" + index + ".");
            if (doorQuestion)
            {
                try { onDoorChosen?.Invoke(index == 0); }
                catch (Exception error)
                {
                    log.Warning("Door choice not committed; choose again: " + error);
                    choiceDecision = -1; choiceMenu.gameObject.SetActive(true);
                    hint.text = "选择未保存，请重试";
                }
            }
        }

        private void LayoutChoices(float scale)
        {
            if (!choiceMenu.gameObject.activeSelf) return;
            float widest = 0;
            for (int i = 0; i < choiceCount; i++)
            {
                var rect = (RectTransform)choiceOptions[i].transform;
                rect.localScale = Vector3.one * scale;
                rect.anchoredPosition = new Vector2(0, -i * ChoiceStep * scale);
                widest = Mathf.Max(widest, rect.rect.width * scale);
            }
            choiceMenu.sizeDelta = new Vector2(widest, choiceCount * ChoiceStep * scale - 6 * scale);
            choiceMenu.anchoredPosition = new Vector2(0, -body.rect.height * scale - 14);
        }
    }
}
