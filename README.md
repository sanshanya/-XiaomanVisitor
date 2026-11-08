# 深空小满：可能很迷醉？

《Probably Stolen / 深空当铺》的小满角色模组。当前稳定版为 **0.0.2**，包含十四次周一剧情、日语配音、中日英文本、十四盘磁带、六款效果饮品、信笺簿与磁带盒。

[Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3835383562)

本仓库保存开发源码、剧情、制作输入与运行素材。**0.0.2 的酒馆仍为“装修中”地图卡片。后续正式开发按作者最新要求：游戏第 3 天起即可进入酒馆。** 该条件已记录在开发计划中，本次环境初始化没有提前实现酒馆玩法。

## 在 Windows 上准备环境

需要已安装的 Probably Stolen、Git（含 Git LFS）、uv、Python 3.12 与 .NET 8 x64 运行时。uv 可以下载缺少的 Python；完整 .NET SDK 不是本项目的构建前提。编译引用直接读取自己的游戏安装目录，仓库不附带游戏程序集。

```powershell
git clone https://github.com/sanshanya/-XiaomanVisitor.git XiaomanVisitor
cd XiaomanVisitor
git lfs install --local
git lfs pull
& .\tools\setup-dev.ps1 -GameRoot 'E:\app\steam\steamapps\common\Probably Stolen'
& .\tools\verify-dev.ps1
```

`setup-dev.ps1` 建立独立 `.venv`，按 `requirements.lock` 校验并安装 Python 依赖，恢复固定版本 Roslyn 4.8.0，核对编译器哈希，并将游戏路径写入不入库的 `.dev/local.json`。其他电脑可通过 `-GameRoot` 指定自己的安装位置。

`verify-dev.ps1` 构建 DLL，运行原生模组扫描与剧情规则检查，再核对三语文本、贴图、高清立绘、42 张手写纸条、107 条录音和离线配音工具。检查只生成本地构建与报告，不安装到游戏、不生成付费录音、不上传工坊。

## 日常开发

```powershell
# 编译；使用 setup-dev 保存的游戏路径
& .\tools\build.ps1

# 运行完整检查
& .\tools\verify-dev.ps1

# 需要实机测试时安装到本机游戏
& .\tools\build.ps1 -Install
```

游戏正在使用不同版本的 DLL 时，安装会要求先保存并退出游戏。不要同时启用本机测试版和工坊版。

稳定基线保存在 `main`，标签 `v0.0.2` 对应当前发布内容；正式酒馆开发使用 `develop/tavern`。之后可从开发分支拆出具体功能分支。工坊发布沿用条目 `3835383562`，单独执行发布流程。

## 目录与素材

| 目录 | 内容 |
| --- | --- |
| `src` | 模组 C# 源码与生成后的剧情绑定 |
| `story` | 剧情终稿、三语纸条、配音脚本、酒馆需求与开发计划 |
| `assets` | 核准立绘、饮品、原录音、制作配置与哈希基线 |
| `package/XiaomanVisitor` | 可打包的运行素材；DLL 由本地构建生成 |
| `tools` | 构建、检查、制作、离线配音与工坊发布工具 |
| `reference` | 仅入库制作所需的小部分原始图层、图标与对白快照 |
| `experiments/handwriting/fonts` | 手写纸条所需悠哉 Regular 及 OFL 授权文件 |

音频、图片与字体通过 **Git LFS** 保存。存档备份、密钥、虚拟环境、游戏反编译参考、旧开发缓存、发布 ZIP 与 Steam 上传状态均不入库。`release`、`outputs` 和其他实验输出在本机保留。

现有音乐与录音随素材一并保存，普通构建不需要原作安装目录或配音服务。历史提取、重新压缩音乐等工具可能仍引用作者本机的原始来源；这些步骤独立于日常构建。配音始终预先生成，模组只播放现成文件；只有明确运行生成命令时才使用本机 `VUILABS_API_KEY` 环境变量。

工坊上传所需的页面草稿、图片顺序与上传状态由作者本地发布目录管理，不由环境准备脚本创建或推送。

`story/酒馆设计` 保留作者提供的 v0.3 原稿、试算表和独立模拟脚本，均为候选设计参考，未接入运行模组。原稿中的开业时点由最新“第 3 天开放”要求取代；具体账期与文案在正式开发时对齐。

酒馆室内候选素材在 `assets/tavern-scene`。背景、现有角色帧、吧台遮挡分开绘制，保留作者 v2 构图；不将角色画死在背景上。`tools/prepare_tavern_scene.py` 核对布局与现有 18 帧立绘，加 `--preview` 可生成离线眨眼、音频口型预览。预览使用已核准录音，不重新请求配音。当前只完成素材准备与验证，游戏里的独立酒馆演出仍待接入。

素材来自《雨音に満ちる夜 / A Night Filled With the Sound of Rain》；字体为悠哉 Regular。相关来源与字体许可保留在运行包说明及 `Licenses` 中。本仓库未为游戏原作素材授予新的开源许可。

更早的制作记录见 `README.txt`；当前开发入口与目录政策以本页为准。
