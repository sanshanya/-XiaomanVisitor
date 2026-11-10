"""Keep the author's final W10 copy, not its superseded candidates."""
import re
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import weekly_story_source as source


class WeekTenSourceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = source.read(10)
        cls.raw = (ROOT / cls.data['author_source']).read_text(encoding='utf-8')

    def test_final_authored_lines_are_verbatim_and_draft_voices(self):
        rows = re.findall(r'(?m)^\| (xm_\w+) \| [^|]+ \| ([^\n]+) \|$', self.raw)
        self.assertEqual(len(rows), 15)
        for key, text in rows:
            self.assertEqual(self.data['lines'][key]['zh'], text)
        self.assertTrue(all(row['defer_voice'] for row in self.data['lines'].values()))
        self.assertNotIn('应该不能长久', self.data['lines']['xm_10_05']['zh'])
        self.assertIn('等那家店的折扣期过去', self.data['lines']['xm_10_05']['zh'])
        self.assertIn('三割', self.data['lines']['xm_10_03']['ja'])
        self.assertIn('thirty percent of the usual price', self.data['lines']['xm_10_03']['en'])

    def test_final_ice_maker_letter_has_no_old_explanation_or_tape_sentence(self):
        final = self.raw.split('## 次日纸条定稿\n\n', 1)[1].split('\n\n不补', 1)[0]
        letter = self.data['letters']['zh']
        self.assertEqual(final, '\n\n'.join(letter[k] for k in ('title', 'body', 'signature')))
        self.assertTrue(letter['body'].endswith('什么时候能够信用点自由呢。'))
        for discarded in ('舍不得', '真讨厌', '磁带'):
            self.assertNotIn(discarded, letter['body'])

    def test_stock_alternatives_are_not_extra_chats_and_return_story_is_delayed(self):
        chat = self.data['night_chat']
        self.assertEqual(chat['keys'], ['xm_tavern_10_chat_01', 'xm_tavern_10_regular_01'])
        self.assertEqual(chat['encounter_delays'], {'xm_tavern_10_regular_01': 2})
        self.assertEqual((chat['from_day'], chat['min_visit']), (64, 10))
        for prefix in ('xm_tavern_10_chat_supported_', 'xm_tavern_10_chat_neutral_'):
            text = ''.join(r['zh'] for k, r in self.data['lines'].items() if k.startswith(prefix))
            self.assertNotIn('剩下的比想象中多', text)
            self.assertNotIn('上周', text)
        self.assertIn('开发补写', self.data['status'])
