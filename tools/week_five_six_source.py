"""Reviewed weekly overlays; no online services or TTS generation."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(visit):
    path = ROOT / ('story/第五周实现.json' if visit == 5 else 'story/第六周实现.json')
    text = path.read_text(encoding='utf-8')
    data = json.loads(text)
    assert data['schema'] == 1 and data['visit'] == visit
    for key, row in list(data['lines'].items()):
        row['key'] = key
        if key.startswith('xm_tavern_'):
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
