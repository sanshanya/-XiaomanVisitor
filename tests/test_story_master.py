"""The source/compiled/localized story contract, including unvoiced A/B letters."""
import csv
import hashlib
import json
import sys
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools'))
import story_master_source as master
import brewing_source


class StoryMasterTests(unittest.TestCase):
    def test_fourteen_chapters_are_actually_replaced(self):
        story=json.loads((ROOT/'assets/story.json').read_text(encoding='utf-8'))
        self.assertEqual(story['master_sha256'],hashlib.sha256(master.SOURCE.read_bytes()).hexdigest())
        rows={r['key']:r for r in story['lines']}
        supply_overrides=brewing_source.read()['overrides']
        for n,week in master.read().items():
            episode=story['episodes'][n-1]
            self.assertEqual(episode['title'],week['title'])
            expected=[k for k in week['lines'] if k.startswith(f'xm_{n:02d}_') and (k.rsplit('_',1)[-1].isdigit() or k.endswith('_shop') or k=='xm_05_gift')]
            self.assertEqual(episode['opening'],expected)
            self.assertIn(f'xm_{n:02d}_accept',rows)
            self.assertEqual(episode['goodbye'],f'xm_{n:02d}_bye')
            for key,row in week['lines'].items():
                row=supply_overrides.get(key,row) if n==4 else row
                for lang in ('zh','ja','en'):
                    value=row[lang].replace('{0}','150') if n==1 and key=='xm_01_shop' and lang!='en' else row[lang]
                    self.assertEqual(rows[key][lang],value,(key,lang))
        self.assertNotIn('xm_01_05',rows)

    def test_all_letters_and_branches_survive_localization_without_entering_tts(self):
        texts={}
        for lang in ('zh','ja','en'):
            with (ROOT/f'package/XiaomanVisitor/Localization/{lang}.csv').open(encoding='utf-8-sig') as f:
                texts[lang]=dict(csv.reader(f))
        for n,week in master.read().items():
            for variant,letter in week['letters'].items():
                key=f'story_note_{n:02d}'+('_b' if variant=='B' else '')
                for lang,fields in letter.items():
                    for field,value in fields.items():
                        self.assertEqual(texts[lang][key+'_'+field].replace('\\n','\n'),value,(key,lang,field))
        voices=json.loads((ROOT/'story/配音脚本_ja.json').read_text(encoding='utf-8'))['lines']
        self.assertFalse(any('note_' in row['key'] for row in voices))
        self.assertEqual({r['key'] for r in voices if r['key'].startswith('xm_14_')},
                         {'xm_14_01','xm_14_02','xm_14_shop','xm_14_accept','xm_14_bye'})
