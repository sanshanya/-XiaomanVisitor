"""W04 authored text, actual attachment and isolated native adapters stay in sync."""
import csv
import json
import sys
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools'))
import week_four_source


class WeekFourAuthoringTests(unittest.TestCase):
    def test_author_dialogue_letters_and_attachment_are_not_lost(self):
        authored=week_four_source.read()
        story=json.loads((ROOT/'assets/story.json').read_text(encoding='utf-8'))
        script={r['key']:r for r in json.loads((ROOT/'story/配音脚本_ja.json').read_text(encoding='utf-8'))['lines']}
        self.assertEqual(story['week_four_sha256'],authored['sha256'])
        self.assertEqual(story['episodes'][3]['track'],'intro2')
        self.assertEqual(story['episodes'][3]['credits'],300)
        for lang in ('zh','ja','en'):
            with (ROOT/f'package/XiaomanVisitor/Localization/{lang}.csv').open(encoding='utf-8-sig',newline='') as f:
                loc={k:v.replace('\\n','\n') for k,v in csv.reader(f)}
            for key,row in authored['lines'].items():
                self.assertEqual(loc[key],row[lang])
                self.assertTrue(script[key]['audio'] or script[key]['defer_recording'])
            for variant,note in authored['letters'].items():
                for field,value in note[lang].items():
                    self.assertEqual(loc['story_note_04'+('_b' if variant=='B' else '')+'_'+field],value)
        paper=json.loads((ROOT/'package/XiaomanVisitor/Books/story_note_04.json').read_text(encoding='utf-8'))
        self.assertEqual(paper['pages'][1]['elements'][0]['id'],'supply_snapshot')
        self.assertFalse((ROOT/'package/XiaomanVisitor/Books/story_note_04_b.json').exists())

    def test_native_ui_and_inventory_ownership_boundaries(self):
        deck=(ROOT/'src/Runtime/XiaomanTavernCassette.cs').read_text(encoding='utf-8')
        self.assertIn('MachineCassettePlayer.CassettePlayer()',deck)
        self.assertIn('tavernTapeInventories.Contains(dest)',deck)
        self.assertIn('ReferenceEquals(machine,tavernDeck)',deck)
        self.assertNotIn('AudioManager.Instance.PlayMusic(',deck)
        ui=(ROOT/'src/Runtime/XiaomanTavern.cs').read_text(encoding='utf-8')
        self.assertIn('BuildInto(root,true',ui)
        self.assertIn('builder.BeginGrid(columns',ui)
        self.assertNotIn('builder.AddLabel("","xiaoman_tavern_cash")',ui)
        for f in ('XiaomanTavernBubble.cs','XiaomanTavernChoices.cs'):
            source=(ROOT/'src/Runtime'/f).read_text(encoding='utf-8')
            self.assertIn('1080f',source)
            self.assertNotIn('rect.localScale/=',source)
