"""Read W04's approved tab-separated dialogue and verbatim letters. No network/TTS."""
import hashlib
import json
import re
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'story/小满酒馆_第四周_v4.6_剧情与扶持支线修订稿.md'


def read():
    text=SOURCE.read_text(encoding='utf-8-sig')
    english=json.loads((ROOT/'story/第四周英文台词.json').read_text(encoding='utf-8'))
    moods={'平静':'Neutral','柔和':'Soft','微笑':'Smile','期待':'Smile','开心':'Smile','得意':'Smile'}
    lines={}
    for row in text.splitlines():
        cells=row.split('\t')
        match=re.fullmatch(r'(xm_\w+)(?: · (\S+))?',cells[0])
        if not match or len(cells)!=3: continue
        key,mood=match.groups()
        lines[key]=dict(key=key,zh=cells[1],ja=cells[2],en=english['lines'][key],mood=moods.get(mood,'Neutral'),defer_voice=True)
    extras={
        'xm_04_03_existing':('前辈，上次谈好的货单我已经留着啦。有什么合适的东西，我们照单子慢慢来就好。','先輩、この前の取引リストは取ってあります。品物があったら、リストに沿って少しずつで大丈夫ですよ。'),
        'xm_tavern_w04_supply_existing':('前辈，货单在这里。今天想给店里送点什么，还是从我这里进货？','先輩、取引リストはこちらです。今日は納品ですか、それともうちから仕入れますか？'),
        'xm_tavern_w04_supply_early':('前辈来啦。我把店里需要的东西列了一张清单，想跟您商量一下。','先輩、いらっしゃい。お店で必要なものをリストにしたので、相談してもいいですか？')}
    for key,(zh,ja) in extras.items():
        lines[key]=dict(key=key,zh=zh,ja=ja,en=english['lines'][key],mood='Soft',defer_voice=True)
    for line in list(lines.values()):
        if not line['key'].startswith('xm_tavern_'): continue
        variant=dict(line,key=line['key']+'_unknown')
        variant['zh']=line['zh'].replace('前辈来啦。','欢迎。').replace('前辈，','').replace('前辈','您')
        variant['ja']=line['ja'].replace('先輩、','').replace('先輩','お客さん')
        variant['en']=line['en'].replace('Senpai, ','').replace(', senpai','')
        lines[variant['key']]=variant
    letters={}
    for variant,start,end in [('A','A｜尚未正式签供销约定','B｜当晚已正式签下供销约定'),('B','B｜当晚已正式签下供销约定','纸条文本约束：')]:
        section=text.split(start,1)[1].split(end,1)[0]
        cn,jp=section.split('日本語 '+variant+'（待审）：',1)
        cn=cn[cn.index('致店长前辈：'):cn.index('——小满')+len('——小满')].strip()
        jp=jp[jp.index('店長先輩へ'):jp.index('\nみちる')+len('\nみちる')].strip()
        note={}
        for lang,value in [('zh',cn),('ja',jp)]:
            parts=value.split('\n\n');note[lang]=dict(title=parts[0],body='\n\n'.join(parts[1:-1]),signature=parts[-1])
        note['en']=english['letters'][variant];letters[variant]=note
    return dict(lines=lines,letters=letters,source=SOURCE.relative_to(ROOT).as_posix(),sha256=hashlib.sha256(text.encode('utf-8')).hexdigest())


def generate():
    c=json.loads((ROOT/'assets/tavern-supply.json').read_text(encoding='utf-8'))
    assert c['schema']==1 and c['unlock_day']==22 and c['group_contract_day']==25
    assert 0<c['player_share_numerator']<c['share_denominator']
    assert c['batch_output']*c['player_share_numerator']//c['share_denominator']>0
    names={'UnlockDay':'unlock_day','ContractDay':'group_contract_day','AuditorBudget':'auditor_budget',
           'InitialCash':'initial_operating_cash','WeeklyBudget':'weekly_purchase_budget','WaterLimit':'weekly_water_limit',
           'JuiceLimit':'weekly_juice_limit','WaterPrice':'water_price','JuicePrice':'juice_price',
           'RecipeWater':'recipe_water','RecipeJuice':'recipe_juice','Output':'batch_output',
           'PlayerNumerator':'player_share_numerator','Denominator':'share_denominator','MakingCost':'batch_making_cost','Wholesale':'wholesale_price'}
    code=['// Generated from assets/tavern-supply.json; author-authorized test values.','namespace XiaomanVisitor { public static class SupplyConfig {']
    for name,key in names.items():
        assert isinstance(c[key],int) and c[key]>0
        code.append(f'public const int {name}={c[key]};')
    for name,key in [('WaterItem','water_item'),('JuiceItem','juice_item'),('Source','source')]: code.append(f'public const string {name}='+json.dumps(c[key])+';')
    code+=['public const long PhoneNumber='+str(c['phone_number'])+'L;','} }','']
    (ROOT/'src/Generated/SupplyConfig.Generated.cs').write_text('\n'.join(code),encoding='utf-8')
