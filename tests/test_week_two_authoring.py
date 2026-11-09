"""Source/translation/glyph regressions specific to the approved W02 overlay."""
import json
import sys
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools'))
import week_two_source
import preview_handwriting as lettering


class WeekTwoAuthoringTests(unittest.TestCase):
    def test_unknown_self_introduction_is_not_replaced_by_a_greeting_variant(self):
        lines=week_two_source.read()['lines']
        self.assertEqual(lines['xm_tavern_greeting_unknown']['zh'],'欢迎光临。我叫小满，这间店刚开门不久。')
        self.assertIn('みちる',lines['xm_tavern_greeting_unknown']['ja'])
        for key in ('xm_tavern_w02_help_intro','xm_tavern_w02_rain_01','xm_tavern_drink_limit'):
            self.assertNotIn('前辈',lines[key+'_unknown']['zh'])
            self.assertNotIn('先輩',lines[key+'_unknown']['ja'])

    def test_second_letter_and_its_kaomoji_are_exact_source_overrides(self):
        notes=json.loads((ROOT/'story/纸条正文.json').read_text(encoding='utf-8'))['notes']
        note=next(n for n in notes if n['visit']==2)
        for lang,value in week_two_source.read()['letters'].items():
            self.assertEqual(note[lang],value['body'])
            for face in ('٩(ˊᗜˋ*)و','(´,,•ω•,,)♡'):
                self.assertIn(face,value['body'])
                self.assertTrue(all(lettering.glyph_path(lettering.FONTS['yozai'],c) for c in face))
        self.assertIsNone(lettering.glyph_path(lettering.FONTS['yozai'],'\U0001f47d'))

    def test_onsite_adapter_has_no_item_or_outing_grant(self):
        source=(ROOT/'src/Runtime/XiaomanTavernTransactions.cs').read_text(encoding='utf-8')
        for forbidden in ('SpawnItem','CreateCocktail','AddCredit','MoveItemTo','lastAfterhourOutingDay','ScavengeDumpingGrounds'):
            self.assertNotIn(forbidden,source)
