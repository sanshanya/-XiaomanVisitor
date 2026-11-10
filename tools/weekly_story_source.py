"""Reviewed weekly overlays; no online services or TTS generation."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCES = {5: '第五周实现.json', 6: '第六周实现.json', 7: '第七周实现.json', 8: '第八周实现.json'}
WEEK_NAMES = {5: 'five', 6: 'six', 7: 'seven', 8: 'eight'}


def read(visit):
    path = ROOT / 'story' / SOURCES[visit]
    text = path.read_text(encoding='utf-8')
    data = json.loads(text)
    assert data['schema'] == 1 and data['visit'] == visit
    chat = data['night_chat']
    assert chat['from_day'] > 0 and 0 <= chat['min_visit'] <= visit
    assert chat['keys'] and all(k.startswith('xm_tavern_') and k in data['lines'] for k in chat['keys'])
    for key, row in list(data['lines'].items()):
        row['key'] = key
        if key.startswith('xm_tavern_') and chat['min_visit'] == 0:
            variant = dict(row)
            variant['key'] = key + '_unknown'
            variant['zh'] = row['zh'].replace('前辈，', '').replace('前辈', '您')
            variant['ja'] = row['ja'].replace('先輩、', '').replace('先輩', 'お客さん')
            variant['en'] = row['en'].replace('Senpai, ', '').replace(', senpai', '')
            data['lines'][key + '_unknown'] = variant
    data['source'] = path.relative_to(ROOT).as_posix()
    data['sha256'] = hashlib.sha256(text.encode('utf-8')).hexdigest()
    return data


def generate():
    c = json.loads((ROOT / 'assets/tavern-luxury.json').read_text(encoding='utf-8'))
    assert c['schema'] == 1 and c['unlock_day'] == 29 and c['preview_day'] == 28
    names = dict(UnlockDay='unlock_day', PreviewDay='preview_day', Onsite='onsite_price',
                 Wholesale='wholesale_price', Output='batch_output', RareCost='rare_kit_cost',
                 MakingCost='making_cost', Water='water', Juice='juice')
    code = ['// Generated from assets/tavern-luxury.json; first-test economics.',
            'namespace XiaomanVisitor { public static class LuxuryConfig {']
    for name, key in names.items():
        assert isinstance(c[key], int) and c[key] > 0
        code.append(f'public const int {name}={c[key]};')
    code += ['} }', '']
    (ROOT / 'src/Generated/LuxuryConfig.Generated.cs').write_text('\n'.join(code), encoding='utf-8')
    c = read(6)['rules']
    assert c['weekly_limit'] is None and not c['monday'] and c['daily_limit'] == 1
    (ROOT / 'src/Generated/ReferralConfig.Generated.cs').write_text(
        '// Generated from story/第六周实现.json.\nnamespace XiaomanVisitor { public static class ReferralConfig {\n'
        f'public const int UnlockDay={c["unlock_day"]}, DailyPercent={c["daily_percent"]};\n' + '} }\n', encoding='utf-8')
