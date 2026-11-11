"""Latest author-requested material exchange overrides; no paid generation."""
import json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'story/供料改造.json'
def read():return json.loads(SOURCE.read_text(encoding='utf-8'))
def apply(chapter):
    for key,row in read()['overrides'].items():
        chapter['lines'][key]=dict(row,key=key,defer_voice=True)
        if key.startswith('xm_tavern_'):
            v=dict(row,key=key+'_unknown',defer_voice=True)
            v['zh']=row['zh'].replace('前辈来啦。','欢迎。').replace('前辈，','').replace('前辈','您')
            v['ja']=row['ja'].replace('先輩、','').replace('先輩','お客さん')
            v['en']=row['en'].replace('Senpai, ','').replace(', senpai','')
            chapter['lines'][v['key']]=v
    return chapter
