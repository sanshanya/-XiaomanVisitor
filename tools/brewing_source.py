"""Latest author-requested material exchange overrides; no paid generation."""
import json
from pathlib import Path
import story_variants
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'story/供料改造.json'
def read():return json.loads(SOURCE.read_text(encoding='utf-8'))
def apply(chapter):
    for key,row in read()['overrides'].items():
        chapter['lines'][key]=dict(row,key=key,defer_voice=True)
        if key.startswith('xm_tavern_'):
            v=story_variants.unacquainted(chapter['lines'][key])
            chapter['lines'][v['key']]=v
    return chapter
