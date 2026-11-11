"""Fit the native chest artwork to its approved 3x3 footprint, offline."""
import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image
import UnityPy

ROOT = Path(__file__).resolve().parents[1]
PACKAGED = 'Textures/Items/xiaoman_wine_crate.png'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, default=Path(r'E:\app\steam\steamapps\common\Probably Stolen'))
    args = parser.parse_args()
    env = UnityPy.load(str(args.game / 'Probably Stolen_Data/resources.assets'))
    matches = [o for o in env.objects if o.type.name == 'Sprite' and o.peek_name() == 'eleanor_box']
    if len(matches) != 1:
        raise ValueError('Expected one native eleanor_box sprite')
    source = matches[0].read().image.convert('RGBA')
    if source.size != (80, 48):
        raise ValueError('Native chest dimensions changed; review before adapting')
    fitted = source.resize((48, 29), Image.Resampling.NEAREST)
    image = Image.new('RGBA', (48, 48))
    image.paste(fitted, (0, 9))
    target = ROOT / 'package/XiaomanVisitor' / PACKAGED
    target.parent.mkdir(parents=True, exist_ok=True)
    image.save(target)
    metadata = {
        'native_atlas': 'Items/items_container', 'native_sprite': 'eleanor_box',
        'native_size': list(source.size),
        'source_rgba_sha256': hashlib.sha256(source.tobytes()).hexdigest(),
        'packaged': PACKAGED, 'size': list(image.size), 'footprint': [3, 3],
        'fit_size': list(fitted.size), 'offset': [0, 9], 'resampling': 'nearest',
        'sha256': hashlib.sha256(target.read_bytes()).hexdigest(),
    }
    (ROOT / 'assets/wine-crate-style.json').write_text(json.dumps(metadata, indent=2) + '\n', encoding='utf-8')
    print('Prepared native chest at 48x48 pixels / 3x3 slots; original game asset unchanged.')


if __name__ == '__main__':
    main()
