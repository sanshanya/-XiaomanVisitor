"""Approved v4.1 overlay; parse authored dialogue once for story and tavern."""
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'story/小满酒馆_第二周_v4.1_实施稿.md'
MOODS = {'平静':'Neutral','柔和':'Soft','微笑':'Smile','开心':'Smile','害羞':'Shy',
         '略慌张':'Worried','得意':'Smile','俏皮':'Smile'}


def read():
    text=SOURCE.read_text(encoding='utf-8-sig')
    translated=json.loads((ROOT/'story/第二周英文台词.json').read_text(encoding='utf-8'))
    lines={}
    for row in text.splitlines():
        if not row.startswith('|'):
            continue
        cells=[cell.strip() for cell in row.strip('|').split('|')]
        for i,cell in enumerate(cells):
            match=re.fullmatch(r'`(xm_[^`]+)`\s*·\s*(.+)',cell)
            if match and len(cells)>i+2:
                key,mood=match.groups()
                lines[key]=dict(key=key,zh=cells[i+1],ja=cells[i+2],en=translated['lines'][key],
                                mood=MOODS[mood],defer_voice=True)
                break
    assert set(lines)==set(translated['lines'])
    # Non-story fallback phrases and explicit unacquainted voice slots.
    for key,zh,ja,en in (
        ('xm_tavern_greeting_plain','欢迎光临。','いらっしゃいませ。','Welcome.'),
        ('xm_tavern_chat_rest','前辈，慢慢坐吧。一起听会儿音乐。','先輩、どうぞゆっくり。少し音楽でも聴きましょう。',
         'Make yourself comfortable. Let\'s listen for a while.')):
        lines[key]=dict(key=key,zh=zh,ja=ja,en=en,mood='Soft',defer_voice=True)
    neutral_variants={'xm_tavern_w02_help_intro','xm_tavern_w02_help_done','xm_tavern_w02_rain_01',
                      'xm_tavern_w02_tape_intro','xm_tavern_drink_limit','xm_tavern_chat_rest'}
    for line in list(lines.values()):
        if line['key'] not in neutral_variants:
            continue
        variant=dict(line,key=line['key']+'_unknown')
        assert variant['key'] not in lines, 'A voice variant must never replace an authored line'
        variant['zh']=line['zh'].replace('店长先生。','').replace('店长先生','您').replace('店长','您').replace('前辈，','').replace('前辈','您')
        variant['ja']=line['ja'].replace('先輩、','').replace('先輩','お客さん').replace('店長さん','お客さん')
        variant['en']=line['en'].replace(', senpai','').replace('Senpai, ','').replace(', shopkeeper','').replace('. could','. Could')
        lines[variant['key']]=variant
    letters={}
    for lang,start,end in (('zh','### 中文｜纸条','### 日本語｜手紙'),('ja','### 日本語｜手紙','`店長先輩`')):
        section=text.split(start,1)[1].split(end,1)[0]
        note='\n'.join(row[1:].lstrip() for row in section.splitlines() if row.startswith('>')).strip()
        parts=note.split('\n\n')
        letters[lang]=dict(title=parts[0],body='\n\n'.join(parts[1:-1]),signature=parts[-1])
    letters['en']=translated['letter']
    return dict(lines=lines,letters=letters,source=SOURCE.relative_to(ROOT).as_posix(),
                sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest())


def generate(data):
    path=ROOT/'src/Generated/TavernDialogue.Generated.cs'
    code=['// Generated from approved weekly implementation sources by prepare_story.py.','using System.Linq;',
          'namespace XiaomanVisitor { public static class TavernDialogue {',
          'public static readonly StoryLineSpec[] Lines = new StoryLineSpec[] {']
    for line in data['lines'].values():
        if line['key'].startswith('xm_tavern_'):
            code.append('new StoryLineSpec('+json.dumps(line['key'])+', Mood.'+line['mood']+'),')
    code+=['};']
    for pool in data['chat_pools']:
        code.append('private static readonly string[] Chat'+str(pool['visit'])+' = new string[] {'+
                    ','.join(json.dumps(key) for key in pool['keys'])+'};')
    sequences=[]
    for pool in data['chat_pools']:
        for key,sequence in pool.get('sequences',{}).items():
            name='Sequence'+str(len(sequences))
            code.append('private static readonly string[] '+name+' = new string[] {'+
                        ','.join(json.dumps(k) for k in sequence)+'};')
            sequences.append((key,name))
    code.append('public static string[] ChatSequence(string key) {')
    for key,name in sequences:
        code.append('if (key=='+json.dumps(key)+') return '+name+';')
    code.append('return new[]{key}; }')
    code.append('public static string[] WeeklyChatPool(int day,int visits) {')
    for pool in reversed(data['chat_pools']):
        code.append(f'if (day>={pool["from_day"]} && visits>={pool["min_visit"]}) return Chat{pool["visit"]};')
    code+=['return null; }','public static Mood MoodFor(string key) { return Lines.FirstOrDefault(l => l.Key == key)?.Mood ?? Mood.Neutral; }',
           'public static string KeyFor(string key, bool known) { return !known && Lines.Any(l => l.Key == key+"_unknown") ? key+"_unknown" : key; }','} }','']
    path.write_text('\n'.join(code),encoding='utf-8')
    price=json.loads((ROOT/'assets/tavern-prices.json').read_text(encoding='utf-8'))
    assert price['schema']==1 and all(isinstance(price[k],int) and 0<price[k]<=100000 for k in ('juice','alcohol'))
    (ROOT/'src/Generated/TavernPrices.Generated.cs').write_text(
        '// Generated from assets/tavern-prices.json; independent of inventory resale value.\n'
        'namespace XiaomanVisitor { public static class TavernPrices {\n'
        f'public const int Juice = {price["juice"]}, Alcohol = {price["alcohol"]};\n'
        'public static int For(CocktailSpec spec) { return LuxuryDrinkCatalog.IsStandard(spec.Key) ? LuxuryConfig.Onsite : spec.Tier == 1 ? Juice : Alcohol; }\n} }\n',encoding='utf-8')
