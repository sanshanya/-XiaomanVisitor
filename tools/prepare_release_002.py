"""Apply the author's four letter revisions and prepare 0.0.2 metadata; no upload or TTS."""
from pathlib import Path
import hashlib
import json
import re
import shutil
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
VERSION="0.0.2"
CHINESE={
1:"""昨天谢谢啦。

一直以为下层区都是些坏蛋，已经做好了准备。

没想到店长这么温柔。

最近总在搬东西，吃的喝的反倒没顾上。

昨天看到你柜台上的磁带机，回去翻了好一会儿，找到了这盘。

在故乡下大雨的时候，我会放这首。

希望你喜欢。""",
2:"""今天把小店玻璃门上的保护膜撕掉了。原来对面那块蓝色广告牌会一直映在玻璃上。

里面还没开灯，从外面看倒像是已经开张了。

不知道上次那盘有没有听。希望您会喜欢。

这盘是整理东西时找到的。

节奏比上次那盘轻柔一些，音量可以调大。""",
3:"""小店就要开张了。

我买了很多的杯子，要把它们全摆上墙花了我很长的时间。

开灯后的感觉棒极了。

店长您的店面也应该找点装饰哦。

下周开始试营业。

今天这盘磁带是我试音响时放的。放在这里给您。

我调酒的手艺还是蛮不错的。

期待店长的光临。""",
4:"""我的小酒馆第一晚关门的时候，店里的客人大部分都还没走。我索性把洗好的杯子又擦了一遍，等着他们离开。

明明玻璃门上写着营业到凌晨两点。等到最后关灯，才发现快要天亮了。

我看店长的店每天挺冷清的，不要太羡慕哦~

这盘是那天开店时放的，送给你。

在这个空间站中能遇见听磁带的同好，真好。""",
}


def save(path,data):
    path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")


def quote(text):
    return "\n".join("> "+line if line else ">" for line in text.splitlines())


def main():
    notes_path=ROOT/"story/纸条译文_0.01.json"
    notes=json.loads(notes_path.read_text(encoding="utf-8"))
    original_later=json.dumps(notes["notes"][4:],ensure_ascii=False)
    by_visit={note["visit"]:note for note in notes["notes"]}
    for number,body in CHINESE.items():
        by_visit[number]["zh"]=body
        by_visit[number]["title_zh"]="店长："
        by_visit[number]["signature_zh"]="——小满。" if number==1 else "——小满"
    by_visit[1]["ja"]=by_visit[1]["ja"].replace("故郷で大雨が降る日は、よくこの曲をかけていたんです。","故郷で大雨が降る日は、この曲をかけていました。")
    by_visit[1]["en"]=by_visit[1]["en"].replace("I used to play this whenever it rained heavily back home.","Back home, I would play this when it rained heavily.")
    if not by_visit[3]["ja"].startswith("小さなお店も、もうすぐ開店です。"):
        by_visit[3]["ja"]="小さなお店も、もうすぐ開店です。\n\n"+by_visit[3]["ja"]
    if not by_visit[3]["en"].startswith("My little shop is almost ready to open."):
        by_visit[3]["en"]="My little shop is almost ready to open.\n\n"+by_visit[3]["en"]
    by_visit[4]["ja"]="""小さな酒場の初日は、閉店の時間になっても、お客さんのほとんどがまだ帰らなくて。洗い終えたグラスをもう一度拭きながら、みんなが帰るのを待っていました。

ガラス戸にはちゃんと午前二時までって書いてあるのに、最後に明かりを消したときには、もう夜が明けそうでした。

店長さんのお店はいつも静かそうですけど、あんまりうらやましがらないでくださいね〜。

その日、お店を開けるときにかけていたテープです。店長さんに。

この宇宙ステーションで、カセットを聴く仲間に出会えるなんて。うれしいです。"""
    by_visit[4]["en"]="""When it was time to close on my little bar's first night, most of the customers were still there. I ended up wiping the clean glasses all over again while I waited for them to leave.

The glass door clearly says we close at two in the morning. By the time I finally turned out the lights, it was nearly dawn.

Your shop always seems pretty quiet. Don't get too jealous~

This is what I played when I opened that night. It's yours.

It's lovely to meet someone else on this space station who listens to tapes."""
    assert json.dumps(notes["notes"][4:],ensure_ascii=False)==original_later
    script_path=ROOT/"story/小满的十四个星期一_v3.4_专属动作台词_TTS演出整合.md"
    script=script_path.read_text(encoding="utf-8-sig")
    tables=re.findall(r"(?m)^\|.*$",script)
    sections=list(re.finditer(r"(?m)^## 第 (\d{2}) 次｜[^\n]+",script))
    assert len(sections)==14
    for number in (4,3,2,1):
        section=sections[number-1]
        start=script.index("**周二｜纸条",section.end())
        jp=script.index("**日本語｜手紙**",start)
        end=script.index("**暗线",jp)
        heading=script[start:script.index("\n",start)]
        note=by_visit[number]
        zh="\n\n".join([note["title_zh"],note["zh"],note["signature_zh"]])
        ja="\n\n".join(["店長さんへ",note["ja"],"みちる"])
        script=script[:start]+heading+"\n\n"+quote(zh)+"\n\n**日本語｜手紙**\n\n"+quote(ja)+"\n\n"+script[end:]
    assert re.findall(r"(?m)^\|.*$",script)==tables, "Spoken and TTS lines changed"
    script_path.write_text(script,encoding="utf-8")
    notes["revision"]=VERSION
    notes["source_sha256"]=hashlib.sha256(script.encode("utf-8")).hexdigest()
    save(notes_path,notes)
    save(ROOT/"story/纸条修订_0.0.2.json",dict(version=VERSION,notes=[by_visit[n] for n in range(1,5)]))

    release=ROOT/"release"/VERSION
    release.mkdir(parents=True,exist_ok=True)
    state=release/"workshop-publish-state.json"
    if not state.exists():
        shutil.copyfile(ROOT/"release/0.01/workshop-publish-state.json",state)
    assert json.loads(state.read_text(encoding="utf-8"))["publishedfileid"]=="3835383562"
    page=json.loads((ROOT/"release/0.01/工坊页面.json").read_text(encoding="utf-8"))
    base_plain,base_bbcode=page["description_plain"],page["description_bbcode"]
    update_plain="0.0.2 更新内容\n\n1. 添加唱片盒和信纸簿，用来收拢信纸和磁带。\n2. 优化了小满的立绘在高分辨率下的表现。"
    update_bbcode="[h2]0.0.2 更新内容[/h2]\n1. 添加唱片盒和[b]信纸簿[/b]，用来收拢信纸和磁带。\n2. 优化了小满的立绘在高分辨率下的表现。"
    page["base_description_bbcode"]=base_bbcode
    page["description_plain"]=base_plain+"\n\n---\n\n"+update_plain
    page["description_bbcode"]=base_bbcode+"\n\n[hr][/hr]\n"+update_bbcode
    page["upload_status"]="prepared_update_not_uploaded"
    save(release/"工坊页面.json",page)
    (release/"workshop-update-note.txt").write_text(update_plain+"\n\n修订前四封纸条，并同步中日英文本与手写信件显示。\n",encoding="utf-8")
    manifest_path=ROOT/"package/XiaomanVisitor/manifest.xml"
    document=ET.parse(manifest_path)
    mod=document.getroot()
    assert mod.findtext("ID")=="sansm.xiaoman.visitor"
    mod.find("ModVersion").text=VERSION
    mod.find("Description").text=page["description_plain"]
    ET.indent(document,space="  ")
    document.write(manifest_path,encoding="utf-8",xml_declaration=True)
    for name in ("使用说明.txt","配音说明.txt","音乐来源.txt"):
        path=ROOT/"package/XiaomanVisitor"/name
        lines=path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
        lines[0]=lines[0].replace("0.01",VERSION)
        path.write_text("".join(lines),encoding="utf-8")
    print("Prepared 0.0.2: four letters, unchanged spoken tables, three-language note sources, appended Workshop description and existing item ID.")


if __name__=="__main__":
    main()
