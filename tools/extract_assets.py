"""Export only the requested standing-portrait assets from a local RPG Maker MZ game."""
from pathlib import Path
from io import BytesIO
import argparse
import hashlib
import json
from PIL import Image, ImageDraw

BASE = Path(__file__).resolve().parents[1]

def decode(path: Path, key: bytes) -> bytes:
    data = path.read_bytes()
    if data.startswith(b"RPGMV"):
        body = bytearray(data[16:])
        for i in range(16):
            body[i] ^= key[i]
        data = bytes(body)
    if not data.startswith(b"\x89PNG\r\n\x1a\n"):
        raise ValueError(f"Not a decoded PNG: {path}")
    return data

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("--group", choices=["girl1", "girl2", "both"], default="both")
    parser.add_argument("--output", type=Path, default=BASE / "reference/source-candidates")
    args = parser.parse_args()
    system = json.loads((args.source / "data/System.json").read_text(encoding="utf-8-sig"))
    key = bytes.fromhex(system["encryptionKey"])
    rows = []
    groups = ["girl1", "girl2"] if args.group == "both" else [args.group]
    for group in groups:
        dest = args.output / group
        dest.mkdir(parents=True, exist_ok=True)
        tiles = []
        for path in sorted((args.source / "img/pictures/busts" / group).glob("*.png*")):
            raw = decode(path, key)
            output = dest / path.name.removesuffix("_")
            output.write_bytes(raw)
            with Image.open(BytesIO(raw)) as im:
                im = im.convert("RGBA")
                rows.append({"source": str(path.relative_to(args.source)), "output": str(output),
                             "size": list(im.size), "bbox": im.getbbox(),
                             "sha256": hashlib.sha256(raw).hexdigest()})
                tile = Image.new("RGBA", (250, 310), (39, 41, 55, 255))
                thumb = im.copy()
                thumb.thumbnail((240, 274), Image.Resampling.NEAREST)
                tile.alpha_composite(thumb, ((250-thumb.width)//2, 28))
                ImageDraw.Draw(tile).text((8, 8), path.name, fill="white")
                tiles.append(tile)
        cols = 5
        sheet = Image.new("RGBA", (cols*250, ((len(tiles)+cols-1)//cols)*310), (22, 23, 32, 255))
        for index, tile in enumerate(tiles):
            sheet.alpha_composite(tile, ((index % cols)*250, (index//cols)*310))
        sheet.convert("RGB").save(args.output / f"{group}-layers.jpg", quality=93)
    (args.output / "extraction.json").write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(rows, ensure_ascii=False, indent=2))

if __name__ == "__main__":
    main()
