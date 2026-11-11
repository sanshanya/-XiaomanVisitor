"""Compile the Wednesday brewing parameters. No network or generation API."""
import json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def generate():
    spec=json.loads((ROOT/'assets/tavern-brewing.json').read_text(encoding='utf-8'))
    assert spec['collection_weekday']=='Wednesday' and spec['delivery_days']==7
    assert spec['sample_value']==10 and spec['bottle_value']==150
    fields={'UnlockDay':'unlock_day','BonusPercent':'bonus_percent','SampleValue':'sample_value','BottleValue':'bottle_value'}
    code=['// Generated from assets/tavern-brewing.json.','namespace XiaomanVisitor { public static class BrewingConfig {']
    for field,key in fields.items():
        assert isinstance(spec[key],int) and 0<spec[key]<=10000
        code.append(f'public const int {field}={spec[key]};')
    code+=['} }','']
    (ROOT/'src/Generated/BrewingConfig.Generated.cs').write_text('\n'.join(code),encoding='utf-8')
if __name__=='__main__':generate()
