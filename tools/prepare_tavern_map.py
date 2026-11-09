"""Prepare the selected tavern cover for the native transit-card image."""
from pathlib import Path
from io import BytesIO
import argparse
import hashlib
import json
from PIL import Image, ImageChops
from extract_assets import decode

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path(r"E:\app\steam\steamapps\common\A Night Filled With the Sound of Rain"))
    args = parser.parse_args()
    digest = lambda data: hashlib.sha256(data).hexdigest()
    recipe = ROOT / "assets/tavern-cover.json"
    if recipe.exists():
        cover = json.loads(recipe.read_text(encoding="utf-8"))
        source = ROOT / cover["source"]
        original = Image.open(source).convert("RGB")
        bounds = cover["crop"]
        left, top, right, bottom = bounds
        assert 0 <= left < right <= original.width and 0 <= top < bottom <= original.height
        assert (right-left)*336 == (bottom-top)*592, "Cover crop must exactly match the native card ratio"
        assert cover["size"] == [592,336] and not cover["padding"]
        room = original.crop(bounds)
        card = room.resize((592,336),Image.Resampling.LANCZOS)
        output = ROOT / "outputs/酒馆封面素材" / f"小满酒馆_封面裁剪_{room.width}x{room.height}.png"
        output.parent.mkdir(parents=True,exist_ok=True)
        room.save(output,optimize=True)
        provenance = dict(source_kind=cover["source_kind"],source_image=cover["source"],
                          source_sha256=digest(source.read_bytes()),filter=cover["filter"],padding=False)
    else:
        source = args.source / "img/pictures/background/06_00.png_"
        settings = json.loads((args.source / "data/System.json").read_text(encoding="utf-8-sig"))
        raw = decode(source, bytes.fromhex(settings["encryptionKey"]))
        original = Image.open(BytesIO(raw)).convert("RGB")
        bounds = ImageChops.difference(original, Image.new("RGB", original.size)).getbbox()
        room = original.crop(bounds)
        room.thumbnail((576, 320), Image.Resampling.NEAREST)
        card = Image.new("RGB", (592, 336))
        card.paste(room, ((card.width - room.width) // 2, (card.height - room.height) // 2))
        provenance = dict(source_game="A Night Filled With the Sound of Rain",source_image="img/pictures/background/06_00.png_",
                          encrypted_source_sha256=digest(source.read_bytes()),decoded_source_sha256=digest(raw),
                          filter="nearest",padding=True)
    target = ROOT / "package/XiaomanVisitor/Textures/Items/xiaoman_tavern_map.png"
    target.parent.mkdir(parents=True, exist_ok=True)
    card.save(target, optimize=True)
    manifest = dict(**provenance,
                    source_size=list(original.size), source_crop=list(bounds), room_size=list(room.size),
                    packaged="Textures/Items/xiaoman_tavern_map.png", size=list(card.size),
                    sha256=digest(target.read_bytes()), bytes=target.stat().st_size,
                    source_art_preserved=True, location_id="sansm.xiaoman.visitor.tavern",
                    image_key="sansm.xiaoman.visitor:xiaoman_tavern_map", phase="week_two_opening", enterable=True,
                    discovery_game_day=3, opening_game_day=8, preview_revealed=True)
    (ROOT / "assets/tavern-map.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: manifest[key] for key in ("packaged", "size", "bytes", "phase", "enterable")}, indent=2))


if __name__ == "__main__":
    main()
