"""Authored unacquainted lines; never rewrite dialogue by removing a name."""
import json
from functools import lru_cache
from pathlib import Path

SOURCE=Path(__file__).resolve().parents[1]/'story/未相识台词.json'


@lru_cache(maxsize=1)
def read():
    data=json.loads(SOURCE.read_text(encoding='utf-8'))
    assert data['schema']==1
    for key,row in data['lines'].items():
        assert key.endswith('_unknown'),key
        assert set(row)=={'zh','ja','en'},key
        assert all(isinstance(text,str) and text.strip() for text in row.values()),key
    return data['lines']


def unacquainted(line):
    key=line['key']+'_unknown'
    return dict(line,key=key,**read()[key])
