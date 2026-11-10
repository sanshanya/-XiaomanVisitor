"""W11's second revision supersedes the signboard and resubmission drafts."""
import re
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import weekly_story_source as source


class WeekElevenSourceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = source.read(11)
        raw = (ROOT / cls.data['author_source']).read_text(encoding='utf-8-sig')
        cls.final = raw.split('第十一周｜修订稿', 1)[1]

    def test_final_dialogue_keeps_three_night_lines_and_no_extra_performance(self):
        rows = re.findall(r'(?m)^(xm_\w+) · ([^\t]+)\t([^\n]+)', self.final)
        self.assertEqual(len(rows), 10)
        moods = {'柔和': 'Soft', '平静': 'Neutral', '微笑': 'Smile'}
        self.assertEqual(set(self.data['lines']), {r[0] for r in rows})
        for key, mood, text in rows:
            self.assertEqual(self.data['lines'][key]['zh'], text)
            self.assertEqual(self.data['lines'][key]['mood'], moods[mood])
        self.assertTrue(all(row['defer_voice'] for row in self.data['lines'].values()))
        self.assertNotIn('xm_tavern_11_chat_04', self.data['lines'])
        self.assertIn('仅为剧情', self.data['status'])

    def test_letter_is_the_exact_uncertainty_revision_not_the_signboard_letter(self):
        final = self.final.split('第十一封纸条｜中文候选\n', 1)[1].split('\n\n情绪说明', 1)[0].strip()
        note = self.data['letters']['zh']
        self.assertEqual(final, '\n\n'.join(note[k] for k in ('title', 'body', 'signature')))
        self.assertIn('味道我很有信心', note['body'])
        self.assertIn('能像前辈一样', note['body'])
        for discarded in ('宣传牌', '磁带', '请帮帮我', '我撑不住了'):
            self.assertNotIn(discarded, note['body'])

    def test_news_preserves_the_authored_promotion_without_a_cash_reward(self):
        final = self.final.split('二、周一｜NEWS\n', 1)[1].split('\n三、第 11 次｜周一当铺来访', 1)[0].strip()
        news = self.data['news']
        self.assertEqual(final, news['title']['zh']+'\n'+news['body']['zh'])
        self.assertEqual((news['day'], news['last_day']), (71, 73))
        self.assertIn('30％', news['body']['ja'])
        self.assertIn('30% of their usual price', news['body']['en'])
        self.assertIn('free drinks worth 500 credits', news['body']['en'])
        self.assertNotIn('xm_w11_news_body', self.data['lines'])
