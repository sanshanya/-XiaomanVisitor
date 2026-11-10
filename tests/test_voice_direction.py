"""Production coverage, safe Japanese performance tags and preserved takes."""
import hashlib
import json
import re
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]


class VoiceDirectionTests(unittest.TestCase):
    def test_approved_production_covers_only_xiaoman_and_includes_gun_reactions(self):
        plan=json.loads((ROOT/'story/配音演出_ja.json').read_text(encoding='utf-8'))
        story=json.loads((ROOT/'assets/story.json').read_text(encoding='utf-8'))
        script=json.loads((ROOT/'story/配音脚本_ja.json').read_text(encoding='utf-8'))
        lines={r['key']:r for r in story['lines']}
        self.assertEqual(set(story['voice_keys']),set(plan['lines']))
        self.assertEqual(set(plan['lines']),{r['key'] for r in script['lines']})
        self.assertFalse(any(k.startswith(('xm_w04_','xm_w06_referral_')) for k in story['voice_keys']))
        self.assertTrue({'xm_gun_plead','xm_gun_holstered'}<=set(story['voice_keys']))
        self.assertEqual((script['voice_id'],script['language'],script['speed']),('ayano','ja',1.1))
        self.assertEqual(story['performance_sha256'],hashlib.sha256((ROOT/'story/配音演出_ja.json').read_bytes()).hexdigest())
        for row in script['lines']:
            direction=plan['lines'][row['key']]
            self.assertEqual(direction['subtitle_ja'],row['subtitle_templates']['ja'])
            self.assertEqual(direction['mood'],lines[row['key']]['mood'])
            self.assertTrue(row['reviewed'])
            self.assertTrue(row['delivery'])
            self.assertNotIn('{',row['generate_text'])
            tags=re.findall(r'\[([^]]+)\]',row['generate_text'])
            self.assertTrue(set(tags)<={'laughs','chuckles','gasps','sighs','inhales','breath','exhale'})
            self.assertFalse(re.search(r'\[[^]]+\]',row['subtitle_templates']['ja']))
            if direction['preserve_audio']:
                self.assertEqual(hashlib.sha256((ROOT/row['audio']).read_bytes()).hexdigest(),direction['approved_audio_sha256'])
            if re.match(r'xm_1[34]_',row['key']):
                self.assertFalse({'sighs','laughs','chuckles'}&set(tags),'the ending must stay restrained')
        self.assertIn('「ろか」',plan['lines']['xm_tavern_12_roka_03']['generate_text'])
        self.assertIn('「露華」',plan['lines']['xm_tavern_12_roka_03']['subtitle_ja'])
