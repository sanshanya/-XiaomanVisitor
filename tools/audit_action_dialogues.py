"""Read-only dialogue audit. Does not edit mod, voice script or localization."""
from pathlib import Path
from collections import Counter
import hashlib
import json
import re
import sys
from export_story_reference import closing_brace

ROOT = Path(__file__).resolve().parents[1]
GAME = Path(r"E:\app\steam\steamapps\common\Probably Stolen")
OUT = ROOT / "story/小满动作台词核对"
DELIVERY = ROOT / "outputs/01a1b47e-04b6-79d2-8f36-75603b1950f7"
FULL_PATH = ROOT / "reference/game-dialogue-audit.full.cs"
FULL = FULL_PATH.read_text(encoding="utf-8-sig")
CACHE = {}


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def body(name):
    if name not in CACHE:
        m = re.search(r"(?m)^.*\bclass " + re.escape(name) + r"\b[^\n]*\n\{", FULL)
        if not m:
            raise ValueError(name)
        opening = FULL.index("{", m.start(), m.end())
        CACHE[name] = (m.start(), FULL[m.start():closing_brace(FULL, opening)])
    return CACHE[name]


def source(name, token):
    offset, code = body(name)
    pos = code.find(token)
    if pos < 0:
        raise ValueError((name, token))
    return f"reference/game-dialogue-audit.full.cs:{FULL.count(chr(10), 0, offset + pos) + 1} ({name})"


def local_source(path, token):
    text = (ROOT / path).read_text(encoding="utf-8-sig")
    return f"{path}:{text.count(chr(10), 0, text.index(token)) + 1}"


def seq(prefix, count, start=1):
    return [f"{prefix}_{i}" for i in range(start, start + count)]


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    OUT.mkdir(parents=True, exist_ok=True)
    DELIVERY.mkdir(parents=True, exist_ok=True)
    loc = {(r["table"], r["key"]): {k: r.get(k, "") for k in ("zh", "ja", "en")}
           for r in json.loads((ROOT / "reference/action-localization.snapshot.json").read_text(encoding="utf-8"))}
    groups, variants = [], []

    def add(id, title, trigger, keys, cls, method, status="可触发", note="", table="Dialogue", order="随机取一条", advice="改成小满专属版"):
        groups.append(dict(id=id, title=title, trigger=trigger, reachability=status, note=note,
                           sequence=order, source=source(cls, method), native_count=len(keys), recommendation=advice,
                           coverage="原版反应，未绑定小满配音" if status in ("可触发", "条件可触发") else "不计入当前必配", custom_keys=[]))
        for i, key in enumerate(keys, 1):
            text = loc.get((table, key))
            if text is None or not text.get("zh"):
                raise ValueError((id, table, key))
            variants.append(dict(action_id=id, title=title, variant=i, reachability=status, table=table, native_key=key, **text,
                                 suggested_audio_id=f"action_{id}_{i:02d}", audio_id_status="建议占位 ID，尚未接入",
                                 design_status="待设计" if status in ("可触发", "条件可触发") else "仅预留",
                                 new_zh="", new_ja="", spoken_ja="", mood="", note=note, sequence=order,
                                 source=source(cls, '"' + key + '"')))

    bargain = [
        ("discount_accept", "接受普通降价", "提出 10%／25% 降价。", "accept_grateful", "CreateAcceptGratefulDialog(int",
         "点击降价即触发，不要求已经成交；与 goodbye_discount 分开。", "本池一条，再接 budget_increase 一条。"),
        ("discount_accept_high", "接受大幅降价", "提出 50%／75% 降价。", "accept_very_grateful", "CreateAcceptGratefulDialog(int",
         "原版按 50% 分档。回礼酒的 10%／25% 是另一套条件。", "本池一条，再接 budget_increase 一条。"),
        ("budget_increase", "降价后追加预算", "降价且启用客户预算；小满当前满足。", "budget_increase", "OfferDiscount(int",
         "最新截图是本池第 3 条。前一段感谢与本段各自随机。", "紧接降价感谢，再从本池取一条。"),
        ("markup_accept", "勉强接受普通涨价", "提出 10%／25% 加价，随机判定为接受。", "accept_reluctant", "CreateAcceptReluctantDialog(int",
         "判定受声望与议价加成影响；小满非事件顾客、非改造体。", "本池一条，再接 budget_decrease 一条。"),
        ("markup_accept_high", "勉强接受大幅涨价", "提出 50%／75% 加价，声望／议价加成足够且接受判定成功。", "accept_very_reluctant", "CreateAcceptReluctantDialog(int",
         "加成不足时接受概率可能为零，但代码保留此分支。", "本池一条，再接 budget_decrease 一条。"),
        ("budget_decrease", "涨价后预算变紧", "加价被接受且启用客户预算。", "budget_decrease", "OfferMarkup(int",
         "不论普通或大幅加价，接受后都有本段。", "紧接涨价接受，再从本池取一条。"),
        ("markup_reject", "拒绝普通涨价并留下", "加价 10%／25%，判定拒绝但不离开。", "reject", "CreateRejectDialog(int", "仍可按流程继续交易。", "随机一条。"),
        ("markup_reject_high", "拒绝大幅涨价并留下", "加价 50%／75%，判定拒绝但不离开。", "hard_reject", "CreateRejectDialog(int", "仍可按流程继续交易。", "随机一条。"),
        ("angry_leave", "生气离开", "涨价引发离开；或诬指罪犯／改造体时抽中离开。", "leave", "CreateLeaveDialog()",
         "与专属 goodbye 不同。", "涨价：离开→bad_word。误指：反驳→离开→bad_word。各池独立随机。"),
        ("bad_word", "表示会传播坏名声", "涨价离开、误指罪犯或改造体。", "talk_bad", "HandleInnocentBlackmail()",
         "误指后没有离开时也会接本段。", "接前面的反驳／离开段，随机一条。"),
        ("blackmail_innocent", "被误指是罪犯", "议价中点击勒索／指控罪犯；小满不是通缉犯。", "innocent_blackmail", "HandleInnocentBlackmail()",
         "CanBlackmail 不检查 arrestable。不能逮捕不等于没有这项反应。", "反驳一条→50% 追加 angry_leave→必接 bad_word。"),
        ("threaten_false_aug", "被误指是改造体", "议价中点击威胁改造体；小满 isAug=false。", "false_threaten", "HandleFalseThreaten()",
         "议价按钮与拔枪分开；CanThreatenAug 不检查 nonShootable。", "反驳一条→50% 追加 angry_leave→必接 bad_word。"),
    ]
    for id, title, trigger, pool, method, note, order in bargain:
        add(id, title, trigger, seq("dialog_bargain_" + pool, 3), "BargainUIManager", method, note=note, order=order)
    add("after_sale_continue", "成交后继续选购", "向她卖出商品后交易未结束，剩余预算≥10。", ["dialog_util_after_sell"], "StoreClientDialogList", "CreateAfterSellingToClientDialog(string",
        note="本次来访首笔先说 accept／accept_return，再接本句；本次后续成交直接说本句。", order="固定一条，不是告别。")
    add("after_sale_low_budget", "成交后余款不足 10", "成交后还剩 1～9 信用点，未自动结束。", ["dialog_util_after_sell_done"], "StoreClientDialogList", "CreateAfterSellingToClientDialog(string",
        note="预算归零走小满 allDoneDialogue，不走本句。", order="固定一条。")
    add("reject_contraband", "拒收违禁品", "把违禁品放进交易区；检查早于普通 wrong_item。", ["dialog_util_no_contraband"], "StoreClientDialogList", "CreateNoContrabandDialog(string",
        note="小满 noAcceptingContraband 固定 true。", order="固定一条。")
    add("remove_filter", "要求先取出滤芯", "玩家水容器本身合需求，但内置滤芯使整个容器不被接受。", ["dialog_water_buyer_filter_inside"], "StoreClient", "TrySayRemoveFilterLine(GameItem",
        note="同一物品有去重。与 wrong_item 同时排队时受优先级影响。", order="固定一条。")
    add("shutter_close", "当面拉下卷帘门", "小满仍在柜台前，玩家结束营业。", seq("dialog_util_shutter", 4), "StoreClientDialogList", "CreateShutterDownClose(string",
        note="第 1、2 条连说；第 3、4 条各自独立，不能当作四选一。", order="约 10%：1→2；约 9%：3；约 81%：4。")
    add("expose_water_base", "识破普通水标签不符", "被接受的水真实纯度≥98%，真实档位是普通水，标签错误且未揭露。", ["dialog_expose_water_basewater"], "ItemFeature", "CreateCustomerConfrontDialog()", status="条件可触发",
        note="小满继承下层居民验水能力。这是品质反应，独立于 wrong_item。", order="摆入交易区或成交前复检；固定一条。")
    add("expose_water_high", "识破优质水标签不符", "被接受的水真实档位为优质水（≥99% 且<99.9%），标签错误且未揭露。", ["dialog_expose_water_highquality"], "ItemFeature", "CreateCustomerConfrontDialog()", status="条件可触发",
        note="同档位的微小纯度差异不一定造成条件不匹配。", order="满足 CanClientExposeThisFeature 后固定一条。")
    add("expose_water_other", "水标签不符的兜底", "水标签不符，真实档位未被前五个分支匹配；例如纯水被标低。", seq("dialog_expose_water_scam", 3), "ItemFeature", "GetRandomWaterScamDiscoveredLine()", status="条件可触发",
        note="原版兜底措辞偏怀疑标签，即使真实水质更好也可能进入。", order="满足揭露条件后随机一条。")

    # Inactive/overridden pools remain as explicitly separated references.
    add("reserved_event_markup", "事件顾客接受涨价", "仅 eventSourceId 非空。", seq("dialog_bargain_accept_reluctant_event", 3), "BargainUIManager", "CreateAcceptReluctantDialog(int", status="当前不可触发",
        note="SetClientSource 只填 sourceModId；小满通过普通队列来访，eventSourceId 为空。", advice="未来改事件顾客后复查")
    add("reserved_aug_markup", "改造体接受议价", "仅 isAug=true。", seq("dialog_bargain_accept_reluctant_aug", 3), "BargainUIManager", "CreateAcceptReluctantDialog(int", status="当前不可触发",
        note="小满非改造体；与被误指改造体的反驳不同。", advice="无需当前配音")
    add("reserved_duplicate_item", "同种物品已经买过", "isRequireVariety=true 且登记过该物品。", ["dialog_util_already_bought"], "StoreClientDialogList", "CreateAlreadyBoughtDialog(string", status="当前不可触发",
        note="小满未启用品类去重，可买多份水。", advice="启用去重后再决定")
    add("reserved_luxury", "拒买奢侈品", "购买标签含 WATER，且非游客／上层阵营。", ["dialogue_no_luxury"], "StoreClient", "IsRefusingLuxuryItem(GameItem", status="当前不可触发",
        note="小满按 willAcceptItem 判需求，clientBuyingTagList 为空，不满足这个额外条件。", advice="改变需求配置后复查")
    add("legacy_out_of_budget", "旧口头预算不足", "旧函数可生成零预算／报出剩余预算两种句子。", ["dialog_util_out_of_budget", "dialog_util_out_of_budget_amount"], "StoreClientDialogList", "CreateOutOfBudgetDialogue(string", status="未见调用点",
        note="完整主程序集只见定义。当前超预算走 ui_nego_budget_warning 界面确认框。", order="按预算是否为零二选一；{0}=预算。", advice="预留，不计当前必配")
    add("replaced_accept", "通用成交确认", "原版 AddBasicDialog 的成交池。", seq("dialog_deal_accepted", 10), "StoreClientDialogList", "acceptedDealDialogues", status="已被专属覆盖",
        note="小满指定 accept／accept_return，不调用 AddBasicDialog。", advice="录现有专属成交句即可")
    add("replaced_done", "通用交易结束", "原版 AddBasicDialog 的结束池。", seq("dialog_deal_done", 14), "StoreClientDialogList", "doneDealDialogues", status="已被专属覆盖",
        note="小满提供 goodbye／goodbye_discount 作为 allDoneDialogue。", advice="录现有专属告别句即可")
    add("replaced_all_done_fallback", "默认完成后的两句告别", "allDoneDialogue=null 才使用。", seq("dialog_util_all_done", 2), "StoreClientDialogList", "CreateDefaultAllDoneDialog(string", status="已被专属覆盖",
        note="小满已经提供 allDoneDialogue。", order="1→2 连说。", advice="录现有专属告别句即可")
    add("inactive_after_player_buy", "店长从她手里买货后的接话", "客户为 SELL 且卖完后还有货。", ["dialog_util_after_buy"], "StoreClientDialogList", "CreateAfterBuyingFromClientDialog(string", status="当前不可触发",
        note="小满当前只买水、食物、日用品；六杯售酒展示测试已经结束。", advice="无需当前配音")
    add("inactive_dirty_water", "识破低于需求的脏水", "真实档位为沟渠水、锈水或幽灵水。", ["dialog_expose_water_gutterflow", "dialog_expose_water_rustwater", "dialog_expose_water_ghostwater"], "ItemFeature", "CreateCustomerConfrontDialog()", status="当前不可触发",
        note="实际纯度不足 98%，需求先拒收为 wrong_item；正常接受入口不到此处。", order="真实档位决定一条。", advice="放宽水需求后复查")
    add("inactive_cigarette", "识破假烟", "收购香烟时检查真假。", ["ins_cig_not_genuine", "ins_cig_issues_with"], "InsCigaretteHelper", "GetCustomerConfrontDialogue(GameItem", status="当前不可触发", table="Mechanic",
        note="小满不买香烟。第 2 句 {0} 为多个问题词的拼接。", order="1→2 连说。", advice="无需当前配音")
    add("inactive_injector", "识破注射器问题", "收购注射器并识破不可用、过期或错误标签。", ["dialog_expose_injector_unusable", "dialog_expose_injector_expired", "dialog_expose_injector_label_wrong"], "ItemFeature", "CreateCustomerConfrontDialog()", status="当前不可触发",
        note="水、食物、日用品需求不接受注射器。", order="真实问题决定一条。", advice="无需当前配音")
    add("inactive_chemical", "识破化学品纯度", "客户可揭露化学品，且接受相应商品。", ["dialog_expose_chemical_" + x for x in ("pharma_grade", "near_pharma", "street_standard", "cut_product", "junk_grade", "starch_grade")], "ItemFeature", "CreateCustomerConfrontDialog()", status="当前不可触发",
        note="小满 CanExposeChem 默认 false，且不买化学品。", order="真实等级决定一条。", advice="无需当前配音")
    add("inactive_label_fallback", "其他标签揭露兜底", "可揭露特征没有对应专用反应。", ["dialog_expose_label_generic"], "ItemFeature", "CreateCustomerConfrontDialog()", status="当前不可触发",
        note="当前需求与下层揭露能力的交集是水，水已有专用分支。", advice="扩充商品范围后复查")
    add("inactive_gun_draw_1", "拔枪反应第一段", "可拿起霰弹枪才进入射击模式。", seq("aug_dialog_unholster", 5, 0), "AugHelper", "GetRandomClientReactionUnholster()", status="当前不可触发",
        note="Shotgun.CanPickup 被 arrestable=false 与 nonShootable=true 阻止。", order="第一池一条→第二池一条。", advice="无需当前配音")
    add("inactive_gun_draw_2", "拔枪反应第二段", "接普通拔枪反应第一段。", seq("aug_dialog_unholster2", 3, 0), "AugHelper", "GetRandomClientReactionUnholster()", status="当前不可触发",
        note="不含四级改造体的另一套反应，小满非改造体。", order="接前一组。", advice="无需当前配音")
    add("inactive_alarm", "按响警报初始反应", "可以举报的顾客按响警报。", ["dialog_arrest_alarm_reaction"], "StoreClientDialogList", "ClientAlarmInitialReactionDialog()", status="当前不可触发",
        note="小满 arrestable=false，入口被阻止。", advice="无需当前配音")
    for id, title, prefix, count, member in [
        ("inactive_arrest", "普通顾客被逮捕", "dialog_arrest_client", 5, "arrestClient ="),
        ("inactive_arrest_criminal", "通缉犯被逮捕", "dialog_arrest_criminal", 6, "arrestClientCriminal ="),
        ("inactive_arrest_blackmarket", "黑市顾客被逮捕", "dialog_arrest_blackmarket", 5, "arrestClientBlackMarket ="),
    ]:
        add(id, title, "满足对应阵营／身份并进入逮捕流程。", seq(prefix, count), "StoreClientDialogList", member, status="当前不可触发",
            note="小满不可逮捕，非通缉犯，属于下层阵营。这里只列 CLIENT，不把 SEC 算成小满。", order="客户一条，之后另一个说话人 SEC 一条。", advice="无需当前配音")

    _, legacy = body("NegociationHelper")
    literals = re.findall(r'\.SetText\("CLIENT", "([^"\n]*)"\)', legacy)
    groups.append(dict(id="legacy_feature_checks", title="旧协商检查英文硬编码", trigger="旧新鲜度、锈蚀、凹痕、过期、封装、金属纯度与验水。",
                       reachability="未见调用点", note="完整主程序集未见该类的外部调用；小满未配置旧 CHECK_*。同文不同分支保留独立记录。",
                       sequence="旧方法各生成一句。", source=source("NegociationHelper", "class NegociationHelper"), native_count=len(literals),
                       recommendation="仅作旧接口参考", coverage="不计入当前必配", custom_keys=[]))
    for i, literal in enumerate(literals, 1):
        variants.append(dict(action_id="legacy_feature_checks", title="旧协商检查英文硬编码", variant=i, reachability="未见调用点", table="C# literal",
                             native_key=f"NegociationHelper.literal_{i:02d}", zh="", ja="", en=literal,
                             suggested_audio_id=f"action_legacy_feature_checks_{i:02d}", audio_id_status="建议占位 ID，尚未接入", design_status="仅预留",
                             new_zh="", new_ja="", spoken_ja="", mood="", note="原代码只有英文，未伪造中文和日文。", sequence="旧检查生成单句。",
                             source=source("NegociationHelper", '"' + literal + '"')))

    silent = [
        ("no_budget_ui_speech", "超预算确认框", "售价高于剩余预算。", "仅界面提示", "ui_nego_budget_warning 是 UI，不是小满说话。", "NegociationUIManager", "OpenConfirmPanel()"),
        ("no_plain_dismiss", "普通手动结束接待", "未实际降价成交时手动送客。", "当前无台词", "tryInterceptDismiss 返回 false，原生直接送客。没有已接入的普通手动告别句。", "PlayerStore", "TryPlayerDismissCurrentClient(bool"),
        ("no_appraisal_impatience", "估价失败后的催促", "估价顾客多次收到不合适估价。", "当前不可触发", "小满 clientIntent=BUY，非估价顾客。未发现普通买家因等待自动催促的对应台词池。", "AppraisalServiceHelper", "OnEstimate(GameItem"),
        ("no_interrogation_glass", "询问／隔离玻璃按钮", "特殊顾客专用按钮。", "当前不可触发", "小满 interogationDialogue 与 glasseDialogue 为空，不是通用随机池。", "NegociationUIManager", "OnInterogationButtonClick()"),
        ("no_barter_seller_checks", "换货与卖家检验回应", "换货，或玩家质疑卖家出售的烟、水、耐久度等。", "当前不可触发", "小满只买货，没有 barterOffer；玩家检验卖家的 CLIENT 回应不属于她买货的正常流程。", "StoreClient", "OnDealAccepted(Dialogue"),
        ("other_speaker", "店长、安保和系统提示", "PLAYER、SEC 角色，UI、教程提示。", "不是小满发言", "这些不计入小满 Ayano 配音。磁带 BGM 也不是台词配音。", "StoreClientDialogList", "ClientArrestDialog()"),
    ]
    for id, title, trigger, status, note, cls, token in silent:
        groups.append(dict(id=id, title=title, trigger=trigger, reachability=status, note=note, sequence="无当前小满口头台词",
                           source=source(cls, token), native_count=0, recommendation="不纳入当前录音", coverage="无新增口头句", custom_keys=[]))
    for id, title, trigger, note in [
        ("no_gift_speech", "次日回礼", "次日获得磁带、纸条或酒。", "纸条是可读物；磁带正常收藏播放；没有小满现场说明对白。"),
        ("no_drink_speech", "玩家喝回礼酒", "使用酒取得三天效果。", "饮用感受已放物品介绍；喝下不弹纸条，也不是小满独白。"),
    ]:
        groups.append(dict(id=id, title=title, trigger=trigger, reachability="当前无台词", note=note, sequence="无口头句",
                           source=local_source("src/XiaomanChapter.cs", "private void TryDeliverDrink"), native_count=0,
                           recommendation="不纳入当前录音", coverage="无新增口头句", custom_keys=[]))

    script = json.loads((ROOT / "story/配音脚本_ja.json").read_text(encoding="utf-8-sig"))
    index = json.loads((ROOT / "package/XiaomanVisitor/Voices/index.json").read_text(encoding="utf-8-sig"))
    voiced = {r["line_key"] for r in index["lines"] if r["language"] == "ja"}
    specs = {
        "intro_1": ("初次来访 1", "初次来访开场。", "平静"),
        "intro_2": ("初次来访 2", "接 intro_1。", "害羞"),
        "intro_3": ("初次来访 3", "接 intro_2，注意到磁带机。", "柔和"),
        "return_1": ("再次来访", "已看过开场，代替三段 intro。", "柔和"),
        "shopping": ("饮用水需求", "开场后说明本次水需求；{0}=预算。", "平静"),
        "shopping_food": ("食物需求", "后续轮换为食物需求；{0}=预算。", "平静"),
        "shopping_household": ("日用品需求", "后续轮换为日用品需求；{0}=预算。", "平静"),
        "accept": ("首次成交确认", "首次有效成交；每次来访只播一次 accept，之后仍会接原版续购或专属告别。", "柔和"),
        "accept_return": ("后续来访成交确认", "已记录首笔成功交易后，再来访的第一笔成交。", "柔和"),
        "goodbye": ("普通自动告别", "成交后预算归零，且无需表达折扣感谢。普通手动送客目前不保证说。", "柔和"),
        "goodbye_discount": ("实际降价成交后告别", "实际卖给她的商品含自愿折扣，自动完成或手动结束时最多一次；只点击降价未成交不算。", "害羞"),
        "wrong_item": ("普通拒收", "非违禁品，但不合当前需求。第一张截图。", "担心"),
        "right_item": ("物品合需求", "合需求物品放入交易区，未被品质揭露占用。", "柔和"),
        "repeat": ("重复水需求", "repeatDialogue 已赋值，当前主程序集未找到读取该字段的调用。", "柔和"),
        "repeat_food": ("重复食物需求", "repeatDialogue 已赋值，当前主程序集未找到读取该字段的调用。", "柔和"),
        "repeat_household": ("重复日用品需求", "repeatDialogue 已赋值，当前主程序集未找到读取该字段的调用。", "柔和"),
    }
    custom = []
    for line in script["lines"]:
        key = line["key"]
        title, trigger, mood = specs[key]
        custom.append(dict(key=key, title=title, trigger=trigger, mood=mood,
                           reachability="已配置，调用未定位" if key.startswith("repeat") else "已接入专属",
                           **line["subtitle_templates"], generic=line["generic"], current_spoken_ja=line["spoken_text"], reviewed=line["reviewed"],
                           voice_status="已有日语音频" if key in voiced else "待定稿，未生成音频",
                           new_zh="", new_ja="", spoken_ja="", design_status="保留" if key in voiced else "待确认",
                           note="{0}=预算；字幕保留变量，口播宜不报具体数额。" if line["generic"] else "",
                           source=local_source("story/配音脚本_ja.json", '"key": "' + key + '"')))
    for title, keys, trigger in [
        ("进场与需求说明", ["intro_1", "intro_2", "intro_3", "return_1", "shopping", "shopping_food", "shopping_household"], "首次三句／返场一句之后，接本次需求。"),
        ("合需求与普通拒收", ["right_item", "wrong_item"], "物品入交易区；违禁品和品质揭露另有入口。"),
        ("现有成交确认", ["accept", "accept_return"], "每次来访第一笔成交，后续接话仍可能用原版。"),
        ("现有告别与折扣感谢", ["goodbye", "goodbye_discount"], "实际折扣成交影响告别；不替代点击降价的两段原版反应。"),
        ("已配置重复需求", ["repeat", "repeat_food", "repeat_household"], "字段已赋值，当前主程序集未见读取调用。"),
    ]:
        groups.append(dict(id="custom_" + keys[0], title=title, trigger=trigger,
                           reachability="已配置，调用未定位" if keys[0] == "repeat" else "已接入专属", note="逐句见现有专属对白页。",
                           sequence="按来访状态和需求选择。", native_count=0, source=local_source("src/XiaomanVisitor.cs", "private StoreClient CreateVisitor"),
                           recommendation="保留或改写现有句", coverage="专属已接入；仅 intro_1 已有音频", custom_keys=keys))

    assert len({g["id"] for g in groups}) == len(groups)
    assert len({(v["table"], v["native_key"]) for v in variants}) == len(variants)
    assert len(custom) == 16 and set(specs) == {c["key"] for c in custom} and len(voiced) == 1
    assert next(v for v in variants if v["native_key"] == "dialog_bargain_budget_increase_3")["zh"] == "这下我又有余钱了，给我看看你还有什么别的东西吧。"
    counts = Counter(v["reachability"] for v in variants)
    assert counts["可触发"] + counts["条件可触发"] == 49, counts
    package = ROOT / "package/XiaomanVisitor/XiaomanVisitor.dll"
    installed = GAME / "Mods/XiaomanVisitor/XiaomanVisitor.dll"
    metadata = dict(audit_date="2026-11-07", mod_version="0.7.0", game=str(GAME),
                    scope="本机原版程序集、小满 0.7.0 源码及中日英本地化的静态核对；未逐个分支实机触发。",
                    game_assembly_sha256=sha(GAME / "Probably Stolen_Data/Managed/Assembly-CSharp.dll"),
                    package_mod_sha256=sha(package), installed_mod_sha256=sha(installed), package_matches_installed=sha(package) == sha(installed),
                    native_counts=dict(counts), current_native_line_count=49,
                    current_native_action_groups=sum(g["native_count"] > 0 and g["reachability"] in ("可触发", "条件可触发") for g in groups),
                    custom_lines=16, recorded_custom_lines=1, screenshot_1="wrong_item", screenshot_2="dialog_bargain_budget_increase_3",
                    write_policy="仅创建核对资料，未修改模组、既有字幕、配音脚本或安装文件。",
                    audio_policy="音频预生成后随包使用；action_* 只是建议占位 ID，尚未接入代码。")
    data = dict(metadata=metadata, groups=groups, variants=variants, custom=custom)
    target = OUT / "小满_动作台词审计_0.7.0.json"
    target.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    txt = ["小满 0.7.0 动作台词核对", "核对日期：2026-11-07", metadata["scope"], "",
           "当前原版反应：20 类动作／分支，共 49 条原版字幕，其中 5 条为验水条件项。",
           "现有专属对白另有 16 条；仅 intro_1 已有日语音频。原版每池通常随机取一条，不表示必须照原数量制作 49 条新台词。",
           "表格新中文、新日文及口播栏留白，可决定逐条改写、合并变体或禁用动作。", "",
           "截图定位", "第一张：wrong_item，已有专属句。", "最新截图：dialog_bargain_budget_increase_3：这下我又有余钱了，给我看看你还有什么别的东西吧。", "",
           "容易漏的串联", "降价：感谢一条→预算增加一条。", "涨价接受：勉强接受一条→预算减少一条。",
           "涨价离开：离开一条→坏名声一条。", "诬指：反驳一条→50% 接离开一条→必接坏名声一条。",
           "首次成交：accept／accept_return→续购、余款不足 10 或专属告别；同次来访后续成交不重复 accept。",
           "当面关门：约 10% 连说 1、2；约 9% 说 3；约 81% 说 4。", "",
           "范围", "不能逮捕、不能拔枪，不影响议价中的误指罪犯和误指改造体按钮。",
           "普通手动送客当前无必播告别；实际折扣成交后才拦截插入 goodbye_discount；预算用尽触发专属告别。",
           "重复需求三句已配置，但主程序集未见读取调用，已标为调用未定位。",
           "纸条、饮酒、BGM、界面提示和 PLAYER／SEC 的话不计作小满配音。",
           "其他模组改变属性或游戏更新后需重查可达性。", "", "一、现有专属对白"]
    for c in custom:
        txt.extend(["", f"[{c['key']}] {c['title']} / {c['reachability']}", "触发：" + c["trigger"], "中文：" + c["zh"],
                    "当前日文字幕：" + c["ja"], "当前日文口播草稿：" + c["current_spoken_ja"], "表情：" + c["mood"] + "；音频：" + c["voice_status"], "出处：" + c["source"]])
    txt.extend(["", "二、所有原版动作与变体（含明确排除项）"])
    for g in groups:
        if g["custom_keys"]:
            continue
        txt.extend(["", f"[{g['id']}] {g['title']} / {g['reachability']}", "触发：" + g["trigger"], "顺序：" + g["sequence"],
                    "备注：" + g["note"], "处理：" + g["recommendation"], "出处：" + g["source"]])
        for v in (v for v in variants if v["action_id"] == g["id"]):
            txt.extend([f"  {v['variant']:02d}. {v['native_key']}", "      原版：" + (v["zh"] or v["en"]), "      日文：" + (v["ja"] or "（原代码无日文）")])
    txt.extend(["", "三、制作约定", "每个录音对稳定 line_key；现有 16 个 key 已接入，action_* 为建议占位，尚未接入。",
                "先定中文和表情，再确认日文字幕及实际口播。{0} 为动态预算，口播宜不读具体金额。",
                "合并原版变体时需在后续实现中统一映射；填表本身不改变游戏或生成录音。", metadata["write_policy"], ""])
    text_path = DELIVERY / "小满_动作台词完整清单_0.7.0.txt"
    text_path.write_text("\n".join(txt), encoding="utf-8-sig")
    print(json.dumps(dict(metadata=metadata, groups=len(groups), variants=len(variants), json=str(target), text=str(text_path)), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
