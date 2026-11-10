"""Reviewed weekly overlays; no online services or TTS generation."""
import hashlib
import json
from pathlib import Path
import story_master_source

ROOT = Path(__file__).resolve().parents[1]
SOURCES = {5: '第五周实现.json', 6: '第六周实现.json', 7: '第七周实现.json', 8: '第八周实现.json', 9: '第九周实现.json', 10: '第十周实现.json', 11: '第十一周实现.json'}
SOURCES.update({n:story_master_source.SOURCE.name for n in (12,13,14)})
WEEK_NAMES = {5: 'five', 6: 'six', 7: 'seven', 8: 'eight', 9: 'nine', 10: 'ten', 11: 'eleven',12:'twelve',13:'thirteen',14:'fourteen'}
NEWS_RULES = {9: ('WeekNineRules', 'HasOpeningNews', 'jizhou_opening_news'),
              11: ('WeekElevenRules', 'HasExtensionNews', 'jizhou_extension_news')}


def read(visit):
    if visit>=12:
        data=story_master_source.chapter(visit)
        keys={12:['xm_tavern_12_business_01'],13:['xm_tavern_13_chat'],14:[]}[visit]
        sequences={}
        if visit==12:
            sequences={f'xm_tavern_12_{topic}_01':[f'xm_tavern_12_{topic}_{i:02d}' for i in range(1,count+1)]
                       for topic,count in (('business',5),('roka',4))}
        data['night_chat']=dict(from_day=1+(visit-1)*7,min_visit=visit,keys=keys,sequences=sequences)
        return data
    path = ROOT / 'story' / SOURCES[visit]
    text = path.read_text(encoding='utf-8')
    data = json.loads(text)
    assert data['schema'] == 1 and data['visit'] == visit
    for key, row in data['lines'].items():
        assert row['mood'] in ('Neutral', 'Soft', 'Shy', 'Smile', 'Closed', 'Worried'), key
        assert all(isinstance(row.get(lang), str) and row[lang].strip() and '\ufffd' not in row[lang]
                   for lang in ('zh', 'ja', 'en')), key
    chat = data['night_chat']
    assert chat['from_day'] > 0 and 0 <= chat['min_visit'] <= visit
    assert chat['keys'] and all(k.startswith('xm_tavern_') and k in data['lines'] for k in chat['keys'])
    for key, sequence in chat.get('sequences', {}).items():
        # Alternate sequences share a logical pool entry/receipt; they are not
        # independently offered as extra conversations.
        assert key in data['lines'] and sequence and sequence[0] == key
        assert len(set(sequence)) == len(sequence) and all(k in data['lines'] for k in sequence)
    for key, delay in chat.get('encounter_delays', {}).items():
        assert key in chat['keys'] and isinstance(delay, int) and 0 <= delay <= 6
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
    return story_master_source.apply(data,visit)


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
    for visit, (name, method, identifier) in NEWS_RULES.items():
        news = read(visit)['news']
        first, last = news['day'], news.get('last_day', news['day'])
        assert isinstance(first, int) and isinstance(last, int) and 0 < first <= last <= first + 2
        assert set(news['title']) == set(news['body']) == {'zh', 'ja', 'en'}
        (ROOT / f'src/Generated/{name}.Generated.cs').write_text(
            f'// Generated from story/{SOURCES[visit]}; newspaper display only.\n'
            f'namespace XiaomanVisitor {{ public static class {name} {{\n'
            f'public const int NewsDay={first}, LastNewsDay={last};\n'
            f'public const string NewsId=XiaomanMod.Id+".{identifier}";\n'
            f'public static bool {method}(int day) {{ return day>=NewsDay && day<=LastNewsDay; }}\n}} }}\n', encoding='utf-8')
