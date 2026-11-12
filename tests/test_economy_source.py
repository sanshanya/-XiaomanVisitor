"""Authored branch coverage, localization, and unacquainted voice boundaries."""
import csv
import json
from pathlib import Path
import re
import sys
import unittest

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools'))
import prepare_economy


class EconomySourceTests(unittest.TestCase):
    def test_all_new_text_is_localized_and_placeholder_safe(self):
        data=prepare_economy.read()
        for key in data['ui']['zh']:
            expected=sorted(re.findall(r'\{\d+\}',data['ui']['zh'][key]))
            for lang in ('ja','en'):
                self.assertEqual(expected,sorted(re.findall(r'\{\d+\}',data['ui'][lang][key])),(key,lang))
        for row in data['lines']:
            for lang in ('ja','en'):
                self.assertEqual(sorted(re.findall(r'\{\d+\}',row['zh'])),sorted(re.findall(r'\{\d+\}',row[lang])),row['key'])

    def test_strangers_never_get_the_familiar_senpai_take(self):
        data=prepare_economy.read();rows={r['key']:r for r in data['lines']}
        for key,row in rows.items():
            if row.get('chapter') or row['key'] in ('xm_eco_shop_13','xm_eco_shop_14','xm_eco_bye') or key.endswith('_unknown'):
                continue
            if any(word in row[lang] for lang,word in (('zh','前辈'),('ja','先輩'),('en','Senpai'))):
                variant=rows[key+'_unknown']
                self.assertTrue(row['unacquainted'])
                self.assertNotIn('前辈',variant['zh']);self.assertNotIn('先輩',variant['ja']);self.assertNotIn('Senpai',variant['en'])
                self.assertEqual(row['mood'],variant['mood'])

    def test_branch_letters_are_separate_native_books_with_their_own_text(self):
        data=prepare_economy.read()
        for key,letter in data['letters'].items():
            paper=json.loads((ROOT/f'package/XiaomanVisitor/Books/{key}.json').read_text(encoding='utf-8'))
            self.assertEqual(paper['id'],key)
            self.assertEqual(paper['item']['id'],'sansm.xiaoman.visitor.'+key)
            for lang in ('zh','ja','en'):
                with (ROOT/f'package/XiaomanVisitor/Localization/{lang}.csv').open(encoding='utf-8',newline='') as handle:
                    texts={k:v.replace('\\n','\n') for k,v in csv.reader(handle)}
                actual='\n\n'.join(texts[key+'_'+field] for field in ('title','body','signature'))
                self.assertEqual(actual,letter[lang])
        tapes=json.loads((ROOT/'assets/audio/cassette-manifest.json').read_text(encoding='utf-8'))
        self.assertEqual(len(tapes),14)
        self.assertEqual(tapes[-1]['key'],'rainysound')
