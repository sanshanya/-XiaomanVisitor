"""Write the first-week script and writer reference. Does not edit any artwork."""
import csv
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "package/XiaomanVisitor"
VERSION = "0.7.0"
DRINKS = [
    ("whale_dream", "拾荒", "基础", "+2次/天", "成交数量≥3件"),
    ("sunset_blue", "幸运", "基础", "+5", "实付≥100信用点"),
    ("bluebird", "幸运", "强效", "+8", "实付≥200信用点"),
    ("cosmo_zone", "拾荒", "强效", "+4次/天", "成交数量≥6件"),
    ("sky_after_rain", "交易声望", "强效", "+20%", "整次主动降价≥25%"),
    ("rainy_blue", "交易声望", "基础", "+10%", "整次主动降价≥10%"),
]

NOTE_ZH = "昨天谢谢啦。水总算买齐了。\n\n这边的灯刚装好，杯子也擦完了。忙了一整天，才想起来忘了准备水。好在你那里还有。\n\n昨天看见了柜台边的磁带机，回去翻了翻行李，找到了这一盘。想着你应该也会喜欢。\n\n以前下大雨的时候，我总爱放这首。有时候雨打在窗上的声音太响，连前奏都听不清。\n\n在这里倒不会有这个烦恼。"

LINES = {
    "zh": {
        "intro_1": "……你好。看见这边还亮着灯，就进来了。",
        "intro_2": "我叫小满。刚搬到附近，有时候还会走错楼层。",
        "intro_3": "咦……你这里居然还有磁带机。",
        "return_1": "又见面啦。今天总算没走错楼层。",
        "shopping": "今天想买些饮用水。普通的就好，带了 {0} 信用点。",
        "shopping_food": "今天想买些吃的。方便存放的就好，带了 {0} 信用点。",
        "shopping_household": "今天想补些日用品。带了 {0} 信用点。",
        "accept": "谢谢。还好赶上了。",
        "accept_return": "嗯，这样就齐啦。谢谢你。",
        "goodbye": "那我先走啦。下次见。",
        "goodbye_discount": "还给我算便宜了……谢谢啦，帮了我一个忙。那我先走啦，下次见。",
        "wrong_item": "这个……今天暂时用不上。",
        "right_item": "嗯，就是这种。",
        "repeat": "普通饮用水就好。别的先不用啦。",
        "repeat_food": "吃的就好。其他东西这次先不买啦。",
        "repeat_household": "日常用的东西就好，今天先把这些买齐。",
        "cfg_interval_tip": "首日即可加入开门时的顾客队列；默认每7天来访一次。同一天最多一次。首次成交前一直收购饮用水，之后按周轮换食物、日用品和水。",
        "gift_tape_desc": "小满留下的一盒旧磁带。标签上的字写得很认真。",
        "gift_note_title": "店长：",
        "gift_note_body": NOTE_ZH,
        "gift_note_signature": "——小满",
        "collection_tape_flavor": "雨夜收藏 {0}/{1} · {2}\n选自《雨夜迷醉时》BGM合集",
        "collection_title_name": "磁带：雨音に満ちる夜 Title",
        "collection_title_flavor": "雨夜收藏 1/14 · 序章\n曲目：雨音に満ちる夜 Title",
        "cocktail_origin": "小满留下的一杯回礼。\n酒杯选自《雨夜迷醉时》。",
        "drink_tier_1": "基础",
        "drink_tier_2": "强效",
        "drink_buff_luck": "幸运 +{0}",
        "drink_buff_tradereputation": "交易获得的正声望 +{0}%",
        "drink_buff_scavenging": "每日拾荒次数 +{0}",
        "drink_buff_duration": "持续3天（含饮用当天）。\n不同类可叠加，各自计时；同类后喝的替换并重新计时。",
        "cfg_voice_enabled": "启用小满配音",
        "cfg_voice_volume": "小满配音音量",
        "cfg_voice_wait": "自动翻页等待小满配音",
        "cfg_voice_language": "小满配音语言",
        "cfg_voice_language_tip": "默认日语，可搭配中文字幕。跟随界面时只播放对应语言录音；缺录音时正常显示字幕。",
    },
    "en": {
        "intro_1": "...Hello. I saw the lights were still on, so I came in.",
        "intro_2": "I'm Xiaoman. I just moved nearby. I still get off on the wrong floor sometimes.",
        "intro_3": "Oh... you even have a cassette player here.",
        "return_1": "Hello again. Found the right floor this time.",
        "shopping": "I'd like some drinking water. Ordinary water is fine. I brought {0} credits.",
        "shopping_food": "I'd like some food that keeps well. I brought {0} credits.",
        "shopping_household": "I need a few household supplies today. I brought {0} credits.",
        "accept": "Thank you. Just made it in time.",
        "accept_return": "That should be everything. Thank you.",
        "goodbye": "I'd better go. See you next time.",
        "goodbye_discount": "You even made it a little cheaper... Thanks, that helps. I'd better go. See you next time.",
        "wrong_item": "This... I don't need it today.",
        "right_item": "Yes, that's the one.",
        "repeat": "Just ordinary drinking water. Nothing else for now.",
        "repeat_food": "Just food. Nothing else this time.",
        "repeat_household": "Everyday supplies. I'll get those sorted out first.",
        "cfg_interval_tip": "Can join the opening queue from day 1; default every 7 days, once per day. Buys drinking water until the first sale, then rotates food, household supplies and water by week.",
        "gift_tape_desc": "An old cassette Xiaoman left you. The label is written with care.",
        "gift_note_title": "Shopkeeper:",
        "gift_note_body": "Thanks for yesterday. I finally got all the water.\n\nThe lights are up and the glasses are clean. After a busy day, I realised I'd forgotten the water. Good thing you still had some.\n\nI noticed your cassette player, so I went through my luggage and found this tape. Thought you might like it too.\n\nI used to play it during heavy rain. Sometimes the rain on the window was so loud I couldn't hear the intro.\n\nThat won't be a problem here.",
        "gift_note_signature": "— Xiaoman",
        "collection_tape_flavor": "Rainy Night Collection {0}/{1} · {2}\nFrom A Night Filled With the Sound of Rain",
        "collection_title_name": "Cassette: Rainy Night · Title",
        "collection_title_flavor": "Rainy Night Collection 1/14 · Prologue\nTrack: A Night Filled With the Sound of Rain · Title",
        "cocktail_origin": "A thank-you gift left by Xiaoman.\nGlass artwork from A Night Filled With the Sound of Rain.",
        "drink_tier_1": "Basic",
        "drink_tier_2": "Strong",
        "drink_buff_luck": "Luck +{0}",
        "drink_buff_tradereputation": "Positive reputation from trades +{0}%",
        "drink_buff_scavenging": "Daily scavenging attempts +{0}",
        "drink_buff_duration": "Lasts 3 days, including today.\nDifferent categories stack with separate timers. Same category: replace and restart its timer.",
        "cfg_voice_enabled": "Enable Xiaoman voice",
        "cfg_voice_volume": "Xiaoman voice volume",
        "cfg_voice_wait": "Wait for Xiaoman voice before auto-advance",
        "cfg_voice_language": "Xiaoman voice language",
        "cfg_voice_language_tip": "Japanese by default, independently of subtitles. Follow UI plays only matching-language recordings; missing recordings keep normal subtitles.",
    },
    "ja": {
        "intro_1": "……こんばんは。まだ明かりがついていたので、入ってきました。",
        "intro_2": "小満といいます。近くに越してきたばかりで、まだ階を間違えることがあるんです。",
        "intro_3": "あれ……ここ、カセットデッキもあるんですね。",
        "return_1": "また来ました。今日は階を間違えませんでしたよ。",
        "shopping": "今日は飲み水を買いたくて。普通ので大丈夫です。{0}クレジット持ってきました。",
        "shopping_food": "今日は日持ちする食べ物が欲しくて。{0}クレジット持ってきました。",
        "shopping_household": "今日は日用品を少し。{0}クレジット持ってきました。",
        "accept": "ありがとう。間に合ってよかった。",
        "accept_return": "うん、これで揃いました。ありがとう。",
        "goodbye": "それじゃ、そろそろ行きますね。また。",
        "goodbye_discount": "安くしてくれたんですね……ありがとう、助かりました。それじゃ、また来ます。",
        "wrong_item": "これは……今日は使わないかな。",
        "right_item": "うん、これです。",
        "repeat": "普通の飲み水だけで大丈夫です。他のものは、また今度。",
        "repeat_food": "食べ物だけで大丈夫です。他のものは、また今度。",
        "repeat_household": "普段使うものを。まずは今日、それを揃えたくて。",
        "cfg_interval_tip": "初日から開店時の客の列に加わります。既定は7日おき、1日1回まで。最初の取引までは飲み水、その後は週ごとに食べ物・日用品・水を買います。",
        "gift_tape_desc": "小満が置いていった古いカセット。ラベルは丁寧に書かれている。",
        "gift_note_title": "店長さんへ：",
        "gift_note_body": "昨日はありがとう。水もやっと揃いました。\n\n明かりもついて、グラスも磨き終わりました。一日中忙しくしていて、水の用意を忘れていたんです。そちらにあってよかった。\n\nカセットデッキを見かけたので、帰って荷物を探したら、この一本が見つかりました。きっと気に入ってくれるかなと思って。\n\n前は大雨の日によく流していました。窓を叩く雨が大きすぎて、前奏も聞こえないことがあったんです。\n\nここでは、その心配はなさそうですね。",
        "gift_note_signature": "——小満",
        "collection_tape_flavor": "雨夜コレクション {0}/{1} · {2}\n『雨音に満ちる夜』BGM集より",
        "collection_title_name": "カセット：雨音に満ちる夜 Title",
        "collection_title_flavor": "雨夜コレクション 1/14 · 序章\n曲名：雨音に満ちる夜 Title",
        "cocktail_origin": "小満が置いていったお礼の一杯。\nグラスの絵は『雨音に満ちる夜』より。",
        "drink_tier_1": "基本",
        "drink_tier_2": "強力",
        "drink_buff_luck": "幸運 +{0}",
        "drink_buff_tradereputation": "取引で得る正の評判 +{0}%",
        "drink_buff_scavenging": "一日の廃品回収回数 +{0}",
        "drink_buff_duration": "3日間（当日を含む）。\n異なる種類は併用でき、期間は別々。同じ種類は置き換わり、その種類の期間だけ更新。",
        "cfg_voice_enabled": "小満のボイスを有効にする",
        "cfg_voice_volume": "小満のボイス音量",
        "cfg_voice_wait": "自動送りでボイスの終了を待つ",
        "cfg_voice_language": "小満のボイス言語",
        "cfg_voice_language_tip": "既定は字幕と独立した日本語。画面の言語に合わせる場合、その言語の録音のみ再生し、未収録の台詞は字幕のみ表示します。",
    },
}


def write_localization():
    for lang, replacements in LINES.items():
        path = PACKAGE / f"Localization/{lang}.csv"
        with path.open(encoding="utf-8-sig", newline="") as f:
            rows = dict(csv.reader(f))
        for obsolete in ("cocktail_offer", "cocktail_accept", "cocktail_repeat"):
            rows.pop(obsolete, None)
        rows.pop("drink_buff", None)
        for key in list(rows):
            if key == "drink_feeling_title" or (key.startswith("drink_") and key != "drink_buff"
                                                and key.endswith(("_feeling", "_buff"))):
                rows.pop(key)
        rows.update(replacements)
        with path.open("w", encoding="utf-8", newline="") as f:
            writer = csv.writer(f, lineterminator="\n")
            writer.writerows((key, value.replace("\n", "\\n")) for key, value in rows.items())
        print(f"{lang}: {len(rows)} keys")


def write_writer_reference():
    with (PACKAGE / "Localization/zh.csv").open(encoding="utf-8", newline="") as f:
        zh = dict(csv.reader(f))
    zh.update(LINES["zh"])
    paragraphs = [f"小满·第一周与交易回礼 / {VERSION} 编剧参考", "",
                  "本文件中的规则供制作使用，不作为任务说明展示给玩家。",
                  "小满只收购物品，不出售酒。六杯同时摆上柜台仅是旧版贴图测试。", "",
                  "第一周首次来访（没有成功买到水时，后续仍收购饮用水）"]
    for key, mood in [("intro_1", "平静"), ("intro_2", "害羞"), ("intro_3", "柔和"),
                      ("shopping", "平静"), ("right_item", "柔和"), ("wrong_item", "担心"),
                      ("repeat", "柔和"), ("accept", "柔和"), ("goodbye", "柔和"),
                      ("goodbye_discount", "害羞，实际降价成交后的结束接待")]:
        paragraphs.extend([f"{key} · {mood}", zh[key], ""])
    paragraphs.extend(["首笔合规饮用水成交后的次日 · 第一封纸条", zh["gift_note_title"], "",
                       NOTE_ZH, "", zh["gift_note_signature"], "",
                       "固定磁带：雨音に満ちる夜 Title / 序章 / 1/14。不是曲名Intro。",
                       "不追加小满解释磁带的台词。未成交则整份回礼顺延。", "", "后续每周收购"])
    for key in ("return_1", "shopping_food", "shopping_household", "repeat_food", "repeat_household", "accept_return"):
        paragraphs.extend([key, zh[key], ""])
    paragraphs.extend(["收购轮换：首笔水成交所在周为水，下一周食物，再下一周日用品，之后重复。",
                       "仅要求类别；没有新增玩家问答或追问选项。", "",
                       "隐藏回礼条件（只供制作参考）",
                       "声望组：降价10%送基础青色雨幕（+10%），25%送强效放晴天空（+20%）。",
                       "拾荒组：3件送基础鲸鱼之梦（每日+2次），6件送强效宇宙空间（每日+4次）。",
                       "幸运组：实付100送基础蔚蓝日落（+5），200送强效幸运青鸟（+8）。",
                       "多类达标时：先强效；同档比较实际数值/该档门槛；比例相同按上次已送达类别轮换。",
                       "首次完全持平按声望→拾荒→幸运顺序，此后从上次类别的下一类开始。",
                       "例如：降价10%且实付200，送强效幸运青鸟；数量9且实付200，送强效宇宙空间。",
                       "六杯均未达门槛则不送酒，不累积跨次接待的数量或金额。",
                       "每次接待只安排一杯，物品越卖越多时更新这杯的种类，次日送达。",
                       "数量累计unitCount，不把每毫升水算一件。实付统计实际卖给小满的合格物品。",
                       "降价率按各物品原生降价按钮百分比×该物品未降价报价，再除整次未降价报价总额。",
                       "仅计真正产生让利的成交；保留品质、市场等原生修正，避免整数价格舍入把25%误判成24%。",
                       "价格低、预算不足、试放物品和玩家向她买东西，都不算主动让利。", "",
                       "酒的介绍与效果保留在物品说明。喝下直接消耗并生效，不另开纸张或感受界面。",
                       "六杯均持续3天：饮用当天及其后两天。幸运、交易声望、拾荒可同时生效，各自计时。",
                       "同类不叠加，后喝的替换该类效果并重新计3天；其他类数值和剩余时间保持原样。"])
    for key, group, tier, effect, condition in DRINKS:
        paragraphs.extend(["", zh.get(f"cocktail_{key}_name", key),
                           f"{group}组 / {tier} / {effect} / 持续3天 / {condition}"])
    paragraphs.extend(["", "原作参照图：assets/drinks/original/cocktail/detail_01.png 至 detail_06.png。",
                       "六种酒均归入烟酒SUBSTANCE/ALCOHOL；不进入食物、饮料、零食收购需求。", "",
                       "未来剧情接入",
                       "Diary、Outro、Outro2保持固定剧情保留；未解锁不得随机发放。",
                       "在真正的剧情节点完成后调用XiaomanMod.CompleteStoryNode(节点ID)。",
                       "节点ID为diary / outro / outro2；解锁本身不发物品，下次有效成交用该磁带替代随机一盘，次日送达。",
                       "灰线仅供作者记忆：灯、杯子、雨与磁带。开场不揭示酒吧或未来离开。",
                       "后期杯子呼应目前只保留为写作方向，尚未编进游戏：",
                       "“收拾东西才发现，有些杯子一次都没用过呢。”",
                       "不要在序章把这句或其解释显示给玩家。"])
    # Use existing project plain-text format; the old 0.4.2/Eleanor archive stays versioned.
    (ROOT / f"story/小满_第一周与回礼规则_{VERSION}.txt").write_text("\n".join(paragraphs) + "\n", encoding="utf-8")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    write_localization()
    write_writer_reference()
