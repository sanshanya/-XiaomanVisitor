"""Protect the final W09 revision against the abandoned early-crisis draft."""
import re
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import weekly_story_source as source


class WeekNineSourceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = source.read(9)
        cls.raw = (ROOT / cls.data['author_source']).read_text(encoding='utf-8-sig')

    def test_final_prosperous_dialogue_replaces_earlier_visit_and_decline(self):
        section = self.raw.split('第九周重写｜再多进一点吧', 1)[1].split('有一些问题', 1)[0]
        rows = list(re.finditer(r'(?m)^(xm_(?:09|tavern_09)_\w+) · ([^\n]+)\n\n([^\n]+)', section))
        self.assertEqual(len(rows), 9)
        for row in rows:
            key, _, value = row.groups()
            if key == 'xm_09_shop':
                value = '今天想买点吃的。带了 {0} 信用点。'
            self.assertEqual(self.data['lines'][key]['zh'], value)
        speech = '\n'.join(row['zh'] for row in self.data['lines'].values())
        for discarded in ('极昼', '新酒廊', '半价券', '推荐屏', '问路'):
            self.assertNotIn(discarded, speech)
        self.assertTrue(all(row['defer_voice'] for row in self.data['lines'].values()))

    def test_news_uses_authors_last_three_tenths_and_500_copy(self):
        final = self.raw.split('1. news改为 **', 1)[1].split('2. 纸条建议改为：', 1)[0].replace('**', '').strip()
        news = self.data['news']
        self.assertEqual(news['title']['zh']+'\n\n'+news['body']['zh'], final)
        self.assertIn('30％', news['body']['ja'])
        self.assertIn('30% of their usual price', news['body']['en'])
        self.assertNotIn('30% off', news['body']['en'])
        self.assertTrue(all('500' in text for text in news['body'].values()))
        self.assertNotIn('xm_w09_news_title', self.data['lines'])
        self.assertNotIn('xm_w09_news_body', self.data['lines'])

    def test_ice_maker_note_is_final_original_without_an_added_tape_reason(self):
        final = '致前辈：'+self.raw.rsplit('致前辈：', 1)[1].split('\n\n——小满', 1)[0]+'\n\n——小满'
        note = self.data['letters']['zh']
        self.assertEqual(final, '\n\n'.join(note[key] for key in ('title', 'body', 'signature')))
        self.assertIn('价格也相当高', note['body'])
        self.assertNotIn('磁带', note['body'])
        self.assertNotIn('清单', note['body'])
        self.assertTrue(note['body'].endswith('前辈也有这样的想法吗？'))
