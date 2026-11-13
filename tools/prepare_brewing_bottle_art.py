"""Import generated bottle art at the native inventory sprite sizes, offline."""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
STYLE=ROOT/'assets/brewing-bottle-art.json'
PACKAGE=ROOT/'package/XiaomanVisitor'


def render(art):
    source=Image.open(ROOT/art['source']).convert('RGBA')
    visible=source.getchannel('A').point(lambda alpha:255 if alpha>=128 else 0)
    bounds=visible.getbbox()
    if bounds is None:
        raise ValueError('Generated bottle is fully transparent')
    left,top,right,bottom=art['native_bounds']
    # Texture import only: nearest-neighbor scale, bounded palette and binary
    # transparency. The authored bottle/label design comes from imagegen.
    scaled=source.crop(bounds).resize((right-left,bottom-top),Image.Resampling.NEAREST)
    alpha=scaled.getchannel('A').point(lambda value:255 if value>=128 else 0)
    pixels=scaled.convert('RGB').quantize(colors=16,method=Image.Quantize.MEDIANCUT).convert('RGBA')
    pixels.putalpha(alpha)
    target=Image.new('RGBA',tuple(art['size']))
    target.paste(pixels,(left,top))
    return target


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check',action='store_true')
    args=parser.parse_args()
    style=json.loads(STYLE.read_text(encoding='utf-8'))
    for art in style['files']:
        path=PACKAGE/art['packaged'];image=render(art)
        source_hash=hashlib.sha256((ROOT/art['source']).read_bytes()).hexdigest()
        if args.check:
            assert source_hash==art['source_sha256'], 'Bottle source changed'
            actual=Image.open(path).convert('RGBA')
            assert actual.size==image.size and actual.tobytes()==image.tobytes(), 'Stale bottle sprite'
            assert hashlib.sha256(path.read_bytes()).hexdigest()==art['sha256']
        else:
            image.save(path)
            art['source_sha256']=source_hash
            art['sha256']=hashlib.sha256(path.read_bytes()).hexdigest()
        assert list(image.size)==art['size']
        assert set(image.getchannel('A').tobytes()).issubset({0,255})
    if not args.check:
        STYLE.write_text(json.dumps(style,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('PASS: two independent bottle sprites at native 16x32 / 16x64; no runtime shape or price changes.')


if __name__=='__main__':main()
