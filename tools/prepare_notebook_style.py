"""Recolor the existing notebook icon palette while preserving every pixel's alpha and layout."""
from pathlib import Path
import hashlib
import json
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
PALETTE = {
    (62,105,121): (181,119,146),
    (69,145,139): (212,155,178),
    (53,82,113): (143,88,115),
    (48,58,65): (98,65,82),
}


def main():
    rows=[]
    for source_key, key in (("tutorial_book","xiaoman_letterbook"),("tutorial_book_full","xiaoman_letterbook_full")):
        source=ROOT / "reference/notebook-icons" / (source_key+".png")
        original=Image.open(source).convert("RGBA")
        assert original.size == (32,48)
        result=original.copy()
        before,after=original.load(),result.load()
        changed=0
        for y in range(original.height):
            for x in range(original.width):
                pixel=before[x,y]
                if pixel[:3] in PALETTE and pixel[3]>1:
                    after[x,y]=(*PALETTE[pixel[:3]],pixel[3])
                    changed+=1
        assert changed>700 and result.getchannel("A").tobytes()==original.getchannel("A").tobytes()
        target=ROOT / "package/XiaomanVisitor/Textures/Items" / (key+".png")
        result.save(target,optimize=True)
        rows.append(dict(source=str(source.relative_to(ROOT).as_posix()),source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                         sprite_key=key,packaged=str(target.relative_to(ROOT/"package/XiaomanVisitor").as_posix()),
                         sha256=hashlib.sha256(target.read_bytes()).hexdigest(),size=[32,48],bytes=target.stat().st_size,changed_pixels=changed))
    style=dict(color="dusty pink",native_atlas_untouched=True,alpha_and_grid_unchanged=True,
               palette=[dict(source=list(key),pink=list(value)) for key,value in PALETTE.items()],files=rows)
    (ROOT/"assets/notebook-style.json").write_text(json.dumps(style,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print("Prepared two pink 32x48 notebook states; native mentor icons, paper details and transparency preserved.")


if __name__=="__main__":
    main()
