"""Keep the authored A/B letters and deferred Japanese consistent across outputs."""
import json
import sys
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools'))
import week_three_source


class WeekThreeAuthoringTests(unittest.TestCase):
    def test_only_two_original_letters_and_one_item(self):
        source=week_three_source.read()
        self.assertEqual(set(source['letters']),{'A','B'})
        note=next(n for n in json.loads((ROOT/'story/纸条正文.json').read_text(encoding='utf-8'))['notes'] if n['visit']==3)
        self.assertEqual(set(note['variants']),{'B'})
        for lang in ('zh','ja','en'):
            self.assertEqual(note[lang],source['letters']['A'][lang]['body'])
            self.assertEqual(note['variants']['B'][lang],source['letters']['B'][lang])
        for variant in ('A','B'):
            self.assertTrue(source['letters'][variant]['zh']['body'].endswith('这是这周的磁带。'))
        self.assertTrue(source['letters']['A']['zh']['body'].startswith('昨天说请前辈来帮忙'))
        self.assertTrue(source['letters']['B']['zh']['body'].startswith('昨天辛苦啦！'))
        self.assertEqual(len(list((ROOT/'package/XiaomanVisitor/Books').glob('story_note_*.json'))),14)

    def test_new_japanese_is_deferred_and_never_replaced_by_an_old_performance(self):
        script=json.loads((ROOT/'story/配音脚本_ja.json').read_text(encoding='utf-8'))
        lines={r['key']:r for r in script['lines']}
        for key,row in week_three_source.read()['lines'].items():
            self.assertEqual(lines[key]['subtitle_templates']['ja'],row['ja'])
            self.assertEqual(lines[key]['generate_text'],row['ja'])
            if not lines[key]['audio']:
                self.assertFalse(lines[key]['reviewed'])
                self.assertTrue(lines[key]['defer_recording'])

    def test_unknown_guests_do_not_gain_an_unearned_identity(self):
        lines=week_three_source.read()['lines']
        for key,row in lines.items():
            if not key.endswith('_unknown'): continue
            for phrase,lang in (('前辈','zh'),('先輩','ja'),('senpai','en')):
                self.assertNotIn(phrase,row[lang].lower())
        self.assertNotIn('自己的店',lines['xm_tavern_w03_work_decline_unknown']['zh'])
        self.assertNotIn('own shop',lines['xm_tavern_w03_work_decline_unknown']['en'])
