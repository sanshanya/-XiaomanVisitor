"""Protect the author's verbatim Chinese/Japanese and confirmed W06 letter choice."""
import re
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import weekly_story_source as source


def quoted(text):
    return '\n'.join(row[1:].lstrip() for row in text.splitlines() if row.startswith('>')).strip()


class WeekFiveSixSourceTests(unittest.TestCase):
    def test_fifth_week_authored_dialogue_is_unchanged(self):
        overlay = source.read(5)
        text = (ROOT / overlay['author_source']).read_text(encoding='utf-8-sig')
        checked = 0
        for row in text.splitlines():
            cells = [c.strip() for c in row.strip('|').split('|')]
            match = re.fullmatch(r'`(xm_[^`]+)` · .+', cells[0])
            if match and len(cells) == 3:
                key = match[1]
                self.assertEqual(overlay['lines'][key]['zh'], cells[1], key)
                self.assertEqual(overlay['lines'][key]['ja'], cells[2], key)
                checked += 1
        self.assertGreaterEqual(checked, 13)

    def test_fifth_week_letter_preserves_original_in_both_languages(self):
        overlay = source.read(5)
        text = (ROOT / overlay['author_source']).read_text(encoding='utf-8-sig')
        for lang, start, end in (
            ('zh', '### 中文｜纸条', '### 日本語｜手紙'),
            ('ja', '### 日本語｜手紙', '**写信动机：**')):
            authored = quoted(text.split(start, 1)[1].split(end, 1)[0])
            fields = overlay['letters'][lang]
            self.assertEqual(authored, '\n\n'.join(fields[k] for k in ('title', 'body', 'signature')))

    def test_sixth_letter_uses_confirmed_recollection_version(self):
        overlay = source.read(6)
        text = (ROOT / overlay['author_source']).read_text(encoding='utf-8-sig')
        authored = text.rsplit('我建议调整为：', 1)[1].split('这版主要有三个改变：', 1)[0].strip()
        fields = overlay['letters']['zh']
        self.assertEqual(authored, '\n\n'.join(fields[k] for k in ('title', 'body', 'signature')))
        self.assertIsNone(overlay['rules']['weekly_limit'])
        self.assertEqual(overlay['rules']['daily_percent'], 25)
        self.assertNotIn('店长', fields['title'])


if __name__ == '__main__':
    unittest.main()
