"""Compile the author's five-act script. Offline; no image edits or voice requests."""
import argparse
import hashlib
import json
import re
import shutil
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "story/支线/永久回购_五幕完整演出剧本_v1.0.md"
REFUSAL_SOURCE = ROOT / "story/支线/永久回购_不开门分支.json"
PRELUDE_SOURCE = ROOT / "story/支线/永久回购_前置.json"
ASSETS = ROOT / "assets/permanent-buyback"
MODULE = ROOT / "side-stories/permanent-buyback"
DIRECTING = json.loads((ASSETS / "direction.json").read_text(encoding="utf-8"))
ID = "sansm.xiaoman.visitor"
VERSION = ET.parse(ROOT / "package/XiaomanVisitor/manifest.xml").getroot().findtext("ModVersion")


def compile_script():
    text = SOURCE.read_text(encoding="utf-8-sig")
    opening = DIRECTING["opening"]
    cues = [dict(kind="shop", seconds=opening["seconds"], opacity=opening["shade"],
                 x=opening["door_x"], y=opening["door_y"]),
            dict(kind="knock", value=opening["knock_repeats"], gains=opening["knock_gains"], retry_gains=opening["retry_knock_gains"])]
    act = 0
    image = 0
    line_number = 0
    deferred_attack = None
    for row in text.splitlines():
        heading = re.match(r"## 第([一二三四五])幕", row)
        if heading:
            act = "一二三四五".index(heading[1]) + 1
            line_number = 0
        if row.startswith("## 结局定格"):
            break
        cg = re.match(r"\*\*【CG\s*0([1-5])", row)
        if cg and int(cg[1]) != image:
            image = int(cg[1])
            shot = DIRECTING["shots"][image - 1]
            if shot["image"] != image:
                raise ValueError("Shot direction is out of order")
            if image == 1:
                cues.append(dict(kind="door_choice", text="开门／不开门"))
            image_cue = dict(kind="image", value=image, seconds=shot["seconds"], act=act,
                             transition=shot["transition"], hold=shot["hold"])
            if image == 3:
                deferred_attack = image_cue
                attack = DIRECTING["attack"]
                cues.append(dict(kind="impact", value=attack["beats"], seconds=attack["blackout_seconds"], hold=attack["blackout_seconds"]))
            else:
                cues.append(image_cue)
        spoken = re.fullmatch(r'(?:\*\*)?“(.*?)”(?:\*\*)?', row)
        if spoken:
            if not act:
                raise ValueError("Spoken line outside an act")
            line_number += 1
            cues.append(dict(kind="line", text=spoken[1], act=act,
                             key=f"buyback_{act:02}_{line_number:02}"))
            segment = next((part for part in DIRECTING["attack"]["segments"] if part["key"] == cues[-1]["key"]), None)
            if segment:
                if deferred_attack is None:
                    raise ValueError("Attack segment is missing its delayed CG")
                ordinal = DIRECTING["attack"]["segments"].index(segment) + 1
                cues[-1].update(value=ordinal, transition="attack_segment", impact_seconds=segment["blood_at"],
                                seconds=segment["end"]-segment["start"])
                if ordinal == 3:
                    cues.append(deferred_attack); deferred_attack = None
            if spoken[1] == DIRECTING["beauty_choice"]["after"]:
                cues.append(dict(kind="reply_choice", key="beauty", options=DIRECTING["beauty_choice"]["options"]))
        elif row == "*停顿两秒。*":
            cues.append(dict(kind="wait", seconds=2.0))
        elif row == "*停顿一秒。*":
            cues.append(dict(kind="wait", seconds=1.0))
        elif row == "*停顿半秒。*":
            cues.append(dict(kind="wait", seconds=0.5))
        elif row == "*短暂停顿。*":
            cues.append(dict(kind="wait", seconds=0.6))
        elif row == "*一声尖锐的金属撞击。*":
            pass  # The three impacts now follow the three recorded requests during the shout.
        elif row == "*画面逐渐变黑。*":
            cues.extend([dict(kind="black", seconds=DIRECTING["ending_fade_seconds"]), dict(kind="lock", seconds=4.0)])
        elif row == "*很久以后，响起磁带机的机械启动声。*":
            cues.append(dict(kind="tape", seconds=1.0))
    title = re.search(r"(?m)^\*\*(隐藏结局：[^*\r\n]+)\*\*$", text)
    if title is None:
        raise ValueError("Missing authored hidden ending title")
    cues.append(dict(kind="title", text=title[1], seconds=1.5))
    counts = {n: sum(c.get("act") == n and c["kind"] == "line" for c in cues) for n in range(1, 6)}
    if any(n == 0 for n in counts.values()):
        raise ValueError(f"Incomplete acts: {counts}")
    if [c["value"] for c in cues if c["kind"] == "image"] != [1, 2, 3, 4, 5]:
        raise ValueError("CG order must remain 1–5")
    return dict(schema=1, language="zh", dialogue_mode=DIRECTING["dialogue_mode"],
                source_sha256=hashlib.sha256(text.encode()).hexdigest(),
                japanese_attack_reference="返して……返してよ……！\n私の人生……返せよ……！！\n返せよおお！！！！",
                cues=cues)


def asset_manifest():
    import wave
    from PIL import Image
    images = []
    for number in range(1, 6):
        path = ASSETS / f"cg_{number:02}.png"
        with Image.open(path) as image:
            image.verify()
        with Image.open(path) as image:
            size = image.size
        images.append(dict(file=path.name, width=size[0], height=size[1],
                           sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
    knock = ASSETS / "buyback_door_knock.wav"
    with wave.open(str(knock), "rb") as audio:
        if (audio.getnchannels(), audio.getsampwidth(), audio.getframerate()) != (1, 2, 48000):
            raise ValueError("Door knock must be mono 48kHz PCM16")
        knock_seconds = audio.getnframes() / audio.getframerate()
    knock_source = json.loads((ASSETS / "knock-source.json").read_text(encoding="utf-8"))
    if (knock_source["license"] != "CC0-1.0" or knock_source["knock_count"] != 3
            or hashlib.sha256((ASSETS / knock_source["file"]).read_bytes()).hexdigest() != knock_source["source_sha256"]):
        raise ValueError("Recorded three-knock source or license differs from the reviewed asset")
    if abs(knock_seconds - (knock_source["trim_seconds"][1] - knock_source["trim_seconds"][0])) > 1 / 48000:
        raise ValueError("Door knock must contain exactly the reviewed recording excerpt")
    music = dict(DIRECTING["music"])
    cassette = next(track for track in json.loads((ROOT / "assets/audio/cassette-manifest.json").read_text(encoding="utf-8"))
                    if track["key"] == "intro")
    music_file = ROOT / "package/XiaomanVisitor/Sounds" / music["file"]
    music["sha256"] = hashlib.sha256(music_file.read_bytes()).hexdigest()
    if music["sha256"] != cassette["sha256"] or music["identifier"] != "rain_intro":
        raise ValueError("Intro must preserve the approved second cassette audio")
    music["seconds"] = cassette["seconds"]
    music["source"] = "沿用主模组已审核的第二盘磁带：小满的雨夜·Intro；原字节保留"
    return dict(schema=1, source="作者提供 C:/Users/sansm/Downloads/CG/1.png 至 5.png",
                treatment="原字节保留；无重绘、裁剪或放大", images=images,
                sounds=DIRECTING["native_sounds"], music=music,
                knock_audio=dict(file="buyback_door_knock.wav", seconds=knock_seconds,
                                 sha256=hashlib.sha256(knock.read_bytes()).hexdigest(), recording=knock_source),
                sound_sources={
                    "ambience": "原生 StoreSoundPlayer.storeAmbient（按节点调节环境声）",
                    "knock": "Fabrizio84 / Knocking on Door（CC0-1.0实录；每次点击播放整段，共三次；播放中不叠加）",
                    "door_open": "原生 EmporiumAudioPlayer.airlock_ext_open",
                    "impact": "原生 knife_stab / wound_blood1 / blunt_impact（刀击、溅血、倒地分层）",
                    "transaction": "原生 EmporiumAudioPlayer.audioRegister（实际交易成功声）",
                    "cloth": "原生 cloth_move1 / cloth_move2（靠近与突进）",
                    "blade": "原生 knife_select（露刃轻响）",
                    "lock": "原生 EmporiumAudioPlayer.airlock_ext_close（金属门闭合）",
                    "tape": "原生 cassette_insert（磁带机械启动）"})


def encoded(value):
    return json.dumps(value, ensure_ascii=False, indent=2) + "\n"


def prepare(check=False):
    refusal = json.loads(REFUSAL_SOURCE.read_text(encoding="utf-8-sig"))
    if refusal.get("schema") != 1 or any(not refusal.get(k) for k in ("retry_line", "news_title", "news_body")):
        raise ValueError("Missing authored refusal branch")
    for path, value in ((MODULE / "script.json", compile_script()), (ASSETS / "assets.json", asset_manifest())):
        content = encoded(value)
        if check:
            if not path.is_file() or path.read_text(encoding="utf-8") != content:
                raise ValueError(f"Stale derived input: {path.relative_to(ROOT)}")
        else:
            path.write_text(content, encoding="utf-8")


def stage_package(stage):
    stage = stage.resolve()
    if stage != (ROOT / "package/XiaomanVisitor").resolve():
        raise ValueError("Buyback must be merged into the existing main package")
    scenes = stage / "Scenes/Buyback"
    scenes.mkdir(parents=True, exist_ok=True)
    for image in asset_manifest()["images"]:
        shutil.copyfile(ASSETS / image["file"], scenes / image["file"])
    shutil.copyfile(MODULE / "script.json", scenes / "script.json")
    shutil.copyfile(REFUSAL_SOURCE, scenes / "refusal.json")
    shutil.copyfile(PRELUDE_SOURCE, scenes / "prelude.json")
    branch = json.loads(REFUSAL_SOURCE.read_text(encoding="utf-8-sig"))
    books = stage / "Books"
    books.mkdir(exist_ok=True)
    paper = json.loads((ROOT / "assets/paper-style.json").read_text(encoding="utf-8"))
    note = dict(id="last_note", title=branch["note_name"], background=ID+":xiaoman_letter_paper",
                width=paper["display_width"], height=paper["display_height"],
                designWidth=paper["design_width"], designHeight=paper["design_height"],
                chrome=[dict(type="button", id="close", action="close", sprite=ID+":xiaoman_note_close",
                             hoverSprite=ID+":xiaoman_note_close_hover", x=297, y=2.5, w=14, h=15)],
                pages=[dict(name="小满的留言", elements=[
                    dict(type="image", id="photo", x=52, y=24, w=210, h=280, fit="fit"),
                    dict(type="text", id="message", text=branch["note_message"], x=30, y=322, w=254, h=44,
                         style="body", fontSize=10, color="#1B304A", wrap=True)])],
                item=dict(id=ID+".last_note", name=branch["note_name"], description="小满留下的一张照片纸条。", sprite="items_misc/handnote", value=0, important=True))
    (books / "last_note.json").write_text(encoded(note), encoding="utf-8")
    shutil.copyfile(ASSETS / "assets.json", scenes / "assets.json")
    shutil.copyfile(ASSETS / "direction.json", scenes / "direction.json")
    sounds = stage / "Sounds"
    sounds.mkdir(exist_ok=True)
    shutil.copyfile(ASSETS / "buyback_door_knock.wav", sounds / "buyback_door_knock.wav")
    from buyback_voice import pack as pack_ending_voice
    pack_ending_voice(stage)
    licenses = stage / "Licenses"
    licenses.mkdir(exist_ok=True)
    (licenses / "永久回购素材来源.txt").write_text(
        "五张CG由作者提供，原文件1.png至5.png，来源与哈希见Scenes/Buyback/assets.json。\n"
        "角色小满来自《雨音に満ちる夜 / A Night Filled With the Sound of Rain》。\n"
        "Sounds/buyback_door_knock.wav截取自Fabrizio84的Knocking on Door录音末组三下，CC0-1.0。\n"
        "原录音：https://freesound.org/people/Fabrizio84/sounds/458007/\n"
        "授权：https://creativecommons.org/publicdomain/zero/1.0/\n"
        "处理：单声道PCM、去直流、短淡入淡出与峰值归一；保留原敲门节奏。原录音哈希和截取范围见Scenes/Buyback/assets.json。\n"
        "Sounds/rain_intro.mp3沿用主模组第二盘磁带“小满的雨夜·Intro”，来自《雨音に満ちる夜》，原音频字节保留，哈希见Scenes/Buyback/assets.json。\n"
        "新增日语对白由作者授权使用VUI Labs Ayano生成（1.1倍速）；第三幕爆发声为作者提供的录音，保留原速。制作源及逐句来源见side-stories/permanent-buyback/voice-script.json，运行包只含离线音频，不含生成接口或凭据。\n"
        "本模组不分发原生游戏程序集或原生音效；运行时引用已安装游戏的音源。\n", encoding="utf-8-sig")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", action="version", version=VERSION)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--stage", type=Path)
    args = parser.parse_args()
    prepare(args.check)
    if args.stage:
        stage_package(args.stage)
    print("PASS: author lines, five CGs and deterministic offline timeline")


if __name__ == "__main__":
    main()
