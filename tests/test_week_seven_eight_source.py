"""Ensure the author's inline correction wins over the older attached draft."""
import re
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import weekly_story_source as source
import story_master_source as master


class WeekSevenEightSourceTests(unittest.TestCase):
    def test_authored_chinese_dialogue_is_preserved(self):
        for visit in (7, 8):
            overlay = source.read(visit)
            text = (ROOT / overlay['author_source']).read_text(encoding='utf-8-sig')
            current = master.read()[visit]['lines']
            checked = 0
            for row in text.splitlines():
                cells = [c.strip() for c in row.strip('|').split('|')]
                match = re.fullmatch(r'`(xm_\w+)` · .+', cells[0])
                if match and match[1] in overlay['lines']:
                    # The accepted master revision takes priority over the older attachment.
                    expected = current.get(match[1], {}).get('zh', cells[-1])
                    self.assertEqual(overlay['lines'][match[1]]['zh'], expected)
                    checked += 1
            self.assertEqual(checked, 9)
            self.assertEqual(overlay['night_chat']['min_visit'], visit)
            self.assertEqual(len(overlay['night_chat']['keys']), 2)
            self.assertTrue(all(row['defer_voice'] for row in overlay['lines'].values()))

    def test_seventh_letter_matches_photo_original(self):
        overlay = source.read(7)
        section = (ROOT / overlay['author_source']).read_text(encoding='utf-8-sig').split('### 1.4 第七封纸条', 1)[1].split('**写作检验：**', 1)[0]
        authored = '\n'.join(row[1:].lstrip() for row in section.splitlines() if row.startswith('>')).strip()
        note = overlay['letters']['zh']
        self.assertEqual(authored, '\n\n'.join(note[k] for k in ('title', 'body', 'signature')))

    def test_eighth_letter_uses_hairclip_override_not_photo_reply(self):
        overlay = source.read(8)
        authored = (ROOT / overlay['letter_source']).read_text(encoding='utf-8').split('\n\n', 1)[1].strip()
        note = overlay['letters']['zh']
        self.assertEqual(authored, '\n\n'.join(note[k] for k in ('title', 'body', 'signature')))
        self.assertIn('前辈，都怪你啦。( ˘•ω•˘ )', note['body'])
        self.assertNotIn('照片', note['body'])
        for lang, letter in overlay['letters'].items():
            self.assertIn('( ˘•ω•˘ )', letter['body'], lang)
