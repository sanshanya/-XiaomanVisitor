"""Read-only lookup of the vanilla customer sprite dimensions and pivot."""
from pathlib import Path
import UnityPy
import json

BASE = Path(__file__).resolve().parents[1]
GAME = Path(r"E:\app\steam\steamapps\common\Probably Stolen\Probably Stolen_Data")
for filename in ["sharedassets1.assets", "sharedassets0.assets", "level1", "level0", "resources.assets"]:
    env = UnityPy.load(str(GAME / filename))
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        try:
            tree = obj.read_typetree()
        except Exception:
            continue
        if "scavFemaleEntries" not in tree:
            continue
        entries = tree.get("lowerFemaleEntries", []) + tree.get("scavFemaleEntries", [])
        print("SpriteDict found in", filename, "entries", len(entries))
        for entry in entries[:4]:
            pointer = entry["sprite"]
            ref = obj.assets_file.getPPtr(pointer["m_FileID"], pointer["m_PathID"]) if hasattr(obj.assets_file, "getPPtr") else None
            print(json.dumps(entry))
        # Resolve actual sprites through the typed PPtr wrapper.
        data = obj.read()
        for entry in (data.lowerFemaleEntries[:2] + data.scavFemaleEntries[:2]):
            sprite = entry.sprite.read()
            print(entry.key, "rect", sprite.m_Rect, "pivot", sprite.m_Pivot, "ppu", sprite.m_PixelsToUnits)
            sprite.image.save(BASE / "reference" / f"vanilla-{entry.key}.png")
        raise SystemExit(0)
print("No SpriteDict found")
