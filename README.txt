深空小满：可能很迷醉？ / 工坊稳定版 0.0.2 / 开发测试版 0.0.3

Git开发入口：README.md。首次准备请运行tools/setup-dev.ps1；完整构建与检查运行tools/verify-dev.ps1。
工坊0.0.2仍为装修中卡片；develop/tavern的0.0.3已接入第3天开放的独立酒馆场景与基础互动。

发布包：release/0.0.2/XiaomanVisitor-0.0.2.zip
原生模组内容：package/XiaomanVisitor
小满的信笺簿：粉色书本外观与原生容器窗口；首次赠信或旧档补发一次，新纸条优先收纳，旧纸条可手动归档。
小满的磁带盒：2×2格的原生盒子外观，内部8×8格；只收小满磁带，允许整盒放入原生磁带机仓库，取带后沿用原生播放。
酒馆扩展需求稿：story/小满酒馆_需求与构思_v0.2.txt（设计文件，尚未接入模组）
原生四档经济规则核对：experiments/tavern-economy/native-economic-facts.json
小满在周一作为额外顾客排队尾，原生剧情与普通顾客的数量和相对顺序保留；邀请快捷键同样不插队。
工坊配置草稿：release/0.0.2/workshop-item.vdf.draft
标题、介绍和三张发布图片已按作者确认内容公开发布到 Steam 创意工坊。
工坊页面：https://steamcommunity.com/sharedfiles/filedetails/?id=3835383562
后续更新沿用 publishedfileid=3835383562，不要新建条目；上传需按作者指示显式执行。
页面原文与 Steam BBCode 见 release/0.0.2/工坊页面介绍.txt 和工坊页面介绍_BBCode.txt。
发布图片见 release/0.01/images，依次为设定图、实机截图、表情图。
首发文本支持中文、日文、英文；现有107条配音保持日语，不重新生成录音。
第6次来访不售酒，次日赠一杯随机酒精版鸡尾酒，替代当次普通交易回礼；酒种固定保存在该次来访记录中。
纸条使用作者提供的蓝色方格信纸，配置在assets/paper-style.json；参考排版见release/0.0.2/纸条预览。

项目目录
src：当前源码。角色、物品、模组与存档标识保持稳定。
package：玩家实际使用的原生模组，只包含清单、DLL、必要素材与使用说明。
assets：制作源素材、已核准原始录音、台词数据及贴图哈希基线。
story：剧情终稿、配音脚本、参考文档和历史说明。
reference：开发参考资料与测试前存档备份，不参与发布。
tools：当前制作、构建和核对工具，不参与发布。
release：当前版本发布包、文件校验和、发布文档与工坊草稿。

构建和检查（PowerShell，游戏默认安装路径见 tools/build.ps1）
  & .\tools\build.ps1
  & .\tools\check.ps1
  & .\tools\build.ps1 -Install
游戏运行时请先保存退出再安装程序更新；构建脚本会检查 DLL 是否正在使用。

Python 工具需要时重新建立环境
  python -m venv .venv
  & .\.venv\Scripts\python.exe -m pip install -r requirements.txt
  & .\.venv\Scripts\python.exe .\tools\check_assets.py
  & .\.venv\Scripts\python.exe .\tools\apply_release_text.py
  & .\.venv\Scripts\python.exe .\tools\check_letter_layout.py
  & .\.venv\Scripts\python.exe .\tools\prepare_handwriting.py
  & .\.venv\Scripts\python.exe .\tools\check_voice_audio.py
  & .\.venv\Scripts\python.exe .\tools\optimize_music.py --apply
  & .\.venv\Scripts\python.exe .\tools\test_voice_pipeline.py
  & .\.venv\Scripts\python.exe .\tools\package_voice_release.py
可加 --with-audio 单独导出按台词键命名的试听 WAV 压缩包；默认不产生这份重复音频。
游戏仍运行、尚未同步安装时，可用 --skip-installed-check 只制作发布文件；这会如实标记尚未核对本机安装。
assets/audio/music-encoding.json保存音乐发布策略。压缩从外部原始MP3生成，原始文件不修改；prepare_cassettes.py重建后会自动应用此策略。
第14盘Hello!保持原文件，其余原声采用保守VBR q1；原始与发布副本的hash分别记录在cassette-manifest.json。
纸条中文修订原稿保存在story/每周纸条修订_0.01_用户原稿.md；中日英纸条在纸条译文_0.01.json，英文对白在英文剧情与动作_0.01.json。
apply_release_text.py仅更新纸条与文字绑定；口头台词的中文/日文与TTS内容保持，已有录音不重新生成。
信件采用悠哉 Regular；原始字体与OFL在experiments/handwriting/fonts中，仅作制作输入，不随模组发布。
prepare_story.py完成纸条更新后会重建Letters内的42张透明文字层；独立重建可运行prepare_handwriting.py。
check_letter_layout.py会核对现有中文、日文和英文文字图层，并以实际发布的图层重新生成预览。
Letters按需读取当前信件与语言，避免一次加载全部图片；原生文本仍保留作缺少图片时的回退。
物品界面文本以assets/item-presentation.json为准；apply_item_presentation.py只同步三语物品描述，不修改剧情和录音。
酒的效果使用原生浅蓝色，描述与风味文字之间留一空行；素材来源保留在说明文件中，物品提示隐藏本模组来源页脚。
版本号统一从 package/XiaomanVisitor/manifest.xml 读取，当前为作者指定的三段式版本 0.0.2。
已移出工作目录的虚拟环境、Python 缓存、检查程序产物和旧发布包均可重新生成。
tools/compilers 和 tools/ilspycmd 保留现有解压工具，避免删除后无法继续构建和查阅参考。

清理结果
删除操作被自动安全检查拒绝（blocked by policy），因此使用了可恢复的归拢方式。
42 项旧发布包、重复备份、下载压缩包和可重建缓存已移到 _待清理开发冗余。
当前源码、原始素材、配音与文档共 401 个文件逐一核对未变，测试前的游戏存档备份保留。
待清理目录约 2173 MiB，尚未释放磁盘空间；后续可在资源管理器中删除整个该目录。
release/0.01/cleanup-report.json 记录具体归拢路径。

发行依据
https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.md#steam-workshop
工坊内容根目录必须直接包含 manifest.xml；上传 package/XiaomanVisitor，不能上传整个项目或 release 文件夹。
ZIP 解压后得到 XiaomanVisitor 文件夹，放入游戏 Mods 目录。

已随0.0.2发布并同步本机：信笺簿、磁带盒，以及夜间地图中的“小满的酒馆（装修中）”地点卡片。
地图仅作预告，暂不可进入；不修改现有十四次剧情或加入酒馆经济系统。
