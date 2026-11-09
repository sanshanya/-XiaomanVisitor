"""Read the approved W03 script; keep both Chinese letters verbatim."""
import hashlib
import json
import re
from pathlib import Path
from week_two_source import MOODS

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'story/小满酒馆_第三周_v4.4_剧情与打工实施稿.md'


def read():
    text=SOURCE.read_text(encoding='utf-8-sig')
    english=json.loads((ROOT/'story/第三周英文台词.json').read_text(encoding='utf-8'))
    lines={}
    for row in text.splitlines():
        if not row.startswith('|'): continue
        cells=[c.strip() for c in row.strip('|').split('|')]
        for i,cell in enumerate(cells):
            m=re.fullmatch(r'`(xm_[^`]+)`\s*·\s*(.+)',cell)
            if m and len(cells)>i+2:
                key,mood=m.groups()
                lines[key]=dict(key=key,zh=cells[i+1],ja=cells[i+2],en=english['lines'][key],mood=MOODS[mood],defer_voice=True)
                break
    key='xm_03_04_already_worked'
    quote=re.search(r'`'+key+r'`：“([^”]+)”',text).group(1)
    lines[key]=dict(key=key,zh=quote,ja='先輩がお店を手伝ってくれたこと、ちゃんと覚えています。忙しい時期が落ち着いたら、また音楽でも聴きに来てくださいね。',
                    en=english['lines'][key],mood='Smile',defer_voice=True)
    assert set(lines)==set(english['lines'])
    for line in list(lines.values()):
        if not line['key'].startswith('xm_tavern_') or '前辈' not in line['zh']: continue
        variant=dict(line,key=line['key']+'_unknown')
        assert variant['key'] not in lines
        variant['zh']=line['zh'].replace('前辈，','').replace('前辈！','').replace('前辈','您')
        variant['ja']=line['ja'].replace('先輩、','').replace('先輩に','お客さんに').replace('先輩の','お客さんの').replace('先輩','お客さん')
        variant['en']=line['en'].replace(', senpai','').replace('Senpai, ','')
        if line['key']=='xm_tavern_w03_work_decline':
            variant.update(zh='嗯，没关系。您也有自己的事情要忙嘛。',
                ja='うん、大丈夫です。ご用事もあるでしょうし。',
                en="That's all right. You have things of your own to take care of too.")
        lines[variant['key']]=variant
    letters={}
    for variant,start,end in (('A','### 4.2 A','### 4.3 B'),('B','### 4.3 B','### 4.4')):
        section=text.split(start,1)[1].split(end,1)[0]
        cn,jp=section.split('**日本語｜手紙',1)
        note={}
        for lang,part in (('zh',cn),('ja',jp)):
            quoted='\n'.join(row[1:].lstrip() for row in part.splitlines() if row.startswith('>')).strip()
            paragraphs=quoted.split('\n\n')
            note[lang]=dict(title=paragraphs[0],body='\n\n'.join(paragraphs[1:-1]),signature=paragraphs[-1])
        note['en']=english['letters'][variant];letters[variant]=note
    return dict(lines=lines,letters=letters,source=SOURCE.relative_to(ROOT).as_posix(),sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest())


def generate():
    config=json.loads((ROOT/'assets/tavern-work.json').read_text(encoding='utf-8'))
    assert config['schema']==1 and config['unlock_day']==15
    assert all(isinstance(config[k],int) and config[k]>0 for k in ('orders_per_shift','order_revenue','order_material','wage','duration_seconds','private_purchase_reserve'))
    assert 0<=config['thanks_percent']<=100 and 0<=config['daily_overflow_orders']<=30
    overrides=config['daily_overrides']
    assert all(int(day)>=15 and isinstance(q,int) and 0<=q<=30 for day,q in overrides.items())
    fields={'UnlockDay':'unlock_day','DailyOrders':'daily_overflow_orders','OrdersPerShift':'orders_per_shift',
            'OrderRevenue':'order_revenue','OrderMaterial':'order_material','Wage':'wage','DurationSeconds':'duration_seconds',
            'ThanksPercent':'thanks_percent','PrivateReserve':'private_purchase_reserve'}
    code=['// Generated from assets/tavern-work.json.','namespace XiaomanVisitor { public static class TavernWorkConfig {',
          'public const string Source='+json.dumps(config['source'])+';']
    code += [f'public const int {name} = {config[key]};' for name,key in fields.items()]
    code += ['public static int DemandCount(int day) { switch(day) {']
    code += [f'case {int(day)}: return {q};' for day,q in sorted(overrides.items(),key=lambda v:int(v[0]))]
    code += ['default: return DailyOrders; } }','} }','']
    (ROOT/'src/Generated/TavernWorkConfig.Generated.cs').write_text('\n'.join(code),encoding='utf-8')
