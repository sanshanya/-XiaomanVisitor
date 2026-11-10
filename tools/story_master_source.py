"""The fourteen-week manuscript owns spoken text and unvoiced letters.

Older weekly inputs retain mechanics and delayed-encounter variants only.
No network access or audio generation is performed here.
"""
import copy
import hashlib
import json
import re
from functools import lru_cache
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'story/小满酒馆_十四周剧情台词整合母稿_v5.4.md'
ENGLISH = ROOT / 'story/十四周母稿英文.json'
MOODS = {'平静': 'Neutral', '柔和': 'Soft', '害羞': 'Shy', '微笑': 'Smile',
         '开心': 'Smile', '期待': 'Smile', '得意': 'Smile', '俏皮': 'Smile',
         '惊喜': 'Smile', '略慌张': 'Worried', '忧虑': 'Worried'}


def raw():
    text = SOURCE.read_text(encoding='utf-8-sig')
    sections = list(re.finditer(r'(?m)^## 第 (\d{2}) 周｜([^\n]+)\n', text))
    assert [int(m[1]) for m in sections] == list(range(1, 15))
    weeks = {}
    for i, match in enumerate(sections):
        n = int(match[1])
        section = text[match.end():sections[i+1].start() if i < 13 else text.index('## 三、')]
        lines = {}
        for row in section.splitlines():
            if not row.startswith('|'):
                continue
            cells = [c.strip() for c in row.strip('|').split('|')]
            for j, cell in enumerate(cells):
                m = re.fullmatch(r'`(xm_[^`]+)`\s*·\s*(.+)', cell)
                if m and len(cells) >= j+3:
                    key, mood = m.groups()
                    assert key not in lines and mood in MOODS, (n, key, mood)
                    lines[key] = dict(key=key, zh=cells[j+1], ja=cells[j+2],
                                      mood=MOODS[mood], defer_voice=True)
                    break
        letters = {}
        heads = list(re.finditer(r'(?m)^#### (中文|日本語)([^\n]*)\n', section))
        for j, head in enumerate(heads):
            lang = 'zh' if head[1] == '中文' else 'ja'
            variant = 'B' if re.search(r'· B｜', head[2]) else 'A'
            part = section[head.end():heads[j+1].start() if j+1 < len(heads) else len(section)]
            quoted = '\n'.join(row[1:].lstrip() for row in part.splitlines() if row.startswith('>')).strip()
            rows = quoted.splitlines()
            assert rows and rows[-1] in ('——小满', 'みちる'), (n, lang, variant)
            letters.setdefault(variant, {})[lang] = dict(title=rows[0], body='\n'.join(rows[1:-1]).strip(), signature=rows[-1])
        assert set(letters) == ({'A', 'B'} if n in (3, 4) else {'A'})
        assert all(set(v) == {'zh', 'ja'} for v in letters.values())
        weeks[n] = dict(title=match[2], lines=lines, letters=letters)
    return weeks


@lru_cache(maxsize=1)
def read():
    weeks = raw()
    english = json.loads(ENGLISH.read_text(encoding='utf-8'))
    for n, week in weeks.items():
        for key, row in week['lines'].items():
            row['en'] = english['lines'][key]
            assert row['en'] and not row['en'].startswith('TODO'), key
        for variant, letter in week['letters'].items():
            letter['en'] = english['letters'][str(n)][variant]
            assert letter['en']['body'] and not letter['en']['body'].startswith('TODO'), (n, variant)
    # Earlier explicit author correction: opening a business is 开业, not 开门.
    for n in (2,):
        for row in weeks[n]['lines'].values():
            row['zh'] = row['zh'].replace('今晚终于要开门啦', '今晚终于要开业啦').replace('这间店刚开门不久', '这间店刚开业不久')
        fields = weeks[n]['letters']['A']['zh']
        fields['body'] = fields['body'].replace('小店终于开门啦', '小店终于开业啦').replace('开门时放的', '开业时放的')
    return weeks


def apply(data, visit):
    """Preserve mechanics/variants while replacing every manuscript-owned text."""
    week = copy.deepcopy(read()[visit])
    prefix = f'xm_{visit:02d}_'
    for key in list(data['lines']):
        if key.startswith(prefix) and key not in week['lines'] and not key.endswith(('_late', '_already_worked', '_existing')):
            del data['lines'][key]
    for key, row in week['lines'].items():
        data['lines'][key] = row
        if key+'_unknown' in data['lines'] and key!='xm_tavern_w03_work_decline':
            variant = dict(row, key=key+'_unknown')
            variant['zh'] = row['zh'].replace('店长先生。', '').replace('店长先生', '您').replace('店长', '您').replace('前辈来啦。', '欢迎。').replace('前辈，', '').replace('前辈！', '').replace('前辈', '您')
            variant['ja'] = row['ja'].replace('先輩、', '').replace('先輩', 'お客さん').replace('店長さん', 'お客さん')
            variant['en'] = row['en'].replace('Senpai, ', '').replace(', senpai', '').replace(', shopkeeper', '')
            data['lines'][variant['key']] = variant
    data['title'] = week['title']
    data['letters'] = week['letters'] if visit in (3, 4) else week['letters']['A']
    data['master_source'] = SOURCE.relative_to(ROOT).as_posix()
    return data


def chapter(visit):
    data = dict(lines={}, source=SOURCE.relative_to(ROOT).as_posix(), sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest())
    return apply(data, visit)
