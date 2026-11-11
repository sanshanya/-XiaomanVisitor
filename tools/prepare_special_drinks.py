"""Build the four authored drink color variants from existing glass sprites."""
import hashlib
import json
from pathlib import Path
from PIL import Image
from item_palette import apply_palette

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / 'package/XiaomanVisitor'


def main():
    path = ROOT / 'assets/luxury-art.json'
    config = json.loads(path.read_text(encoding='utf-8'))
    assert config['schema'] == 2 and {r['key'] for r in config['files']} == {'wonderland', 'spring_sway', 'wonderland_improv', 'roka'}
    for row in config['files']:
        source = ROOT / row['source']
        assert hashlib.sha256(source.read_bytes()).hexdigest() == row['source_sha256'], row['key']
        image = Image.open(source).convert('RGBA')
        assert image.size == (16, 32) and set(image.getchannel('A').tobytes()) <= {0, 255}
        target = PACKAGE / row['packaged']
        assert target.resolve() != source.resolve()
        apply_palette(image, row['palette']).save(target)
        row['sha256'] = hashlib.sha256(target.read_bytes()).hexdigest()
    path.write_text(json.dumps(config, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('Prepared four distinct drink palettes; original six glasses and 1x2 geometry preserved.')


if __name__ == '__main__':
    main()
