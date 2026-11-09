"""Regression boundaries for the UI omissions found during author gameplay."""
import csv
import json
from pathlib import Path
import re
import unittest

ROOT=Path(__file__).resolve().parents[1]


class UiBindingTests(unittest.TestCase):
    def test_opening_dialogue_passes_the_fixed_budget_for_all_locales(self):
        source=(ROOT/'src/Runtime/StoryRuntime.cs').read_text(encoding='utf-8')
        self.assertRegex(source,r'StoryLine\(client,\s*key,\s*spec\.Mood,\s*episode\.Credits\)')
        self.assertRegex(source,r'Line\(client,\s*key,\s*expression,\s*args\)')
        story=json.loads((ROOT/'assets/story.json').read_text(encoding='utf-8'))
        for lang in ('zh','ja','en'):
            with (ROOT/f'package/XiaomanVisitor/Localization/{lang}.csv').open(encoding='utf-8-sig',newline='') as f:
                localized=dict(csv.reader(f))
            for episode in story['episodes']:
                for key in episode['opening']:
                    rendered=localized[key].format(episode['credits'])
                    self.assertIsNone(re.search(r'\{\d+\}',rendered))
                    if '{0}' in localized[key]: self.assertIn(str(episode['credits']),rendered)

    def test_scene_cannot_cover_the_native_backpack_or_own_the_real_conversation(self):
        scene=(ROOT/'src/Runtime/XiaomanTavern.cs').read_text(encoding='utf-8')
        self.assertIn('RenderHandler.current.windowUICanvas.transform',scene)
        self.assertNotIn('actor.Configure(this,CustomUIManager.Instance.GetLayerRoot("meta"))',scene)
        self.assertIn('EmporiumEntry.Instance.ShowAfterhourInv()',scene)
        self.assertNotIn('TransferBackpackToInv()',scene)
        choices=(ROOT/'src/Runtime/XiaomanTavernChoices.cs').read_text(encoding='utf-8')
        self.assertIn('view.enabled=false',choices)
        self.assertIn('XiaomanTavernBubble.Clear(button.onClick)',choices)
        self.assertNotIn('SelectChoice(',choices)
        self.assertNotIn('StartDialogue(',choices)
