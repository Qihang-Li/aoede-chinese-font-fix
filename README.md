# AoEDE Chinese Font Fix · 简体中文字体修复

适用于 **Age of Empires: Definitive Edition（第一代决定版，Steam App 1017900）** 的非官方字体修复与自定义工具。

游戏已经显示简体中文，但部分汉字的字形可能不符合你习惯的简体中文样式。本工具在本机读取游戏字体资源，统一重新绘制其中的汉字，并保留原有字距和排版信息。不是逐个寻找、修补个别字符。

默认字体为 **Aoe CJK Sans**：基于 Adobe 思源黑体简体中文 CN 2.005 制作的静态 TrueType 衍生字体，提供常规和粗体，按 SIL OFL 1.1 分发。具体来源、转换方法、校验值见 [FONT-NOTICES.md](FONT-NOTICES.md)。

## 下载

请从 [GitHub Releases](https://github.com/Qihang-Li/aoede-chinese-font-fix/releases) 下载 `AoEDE-Chinese-Font-Fix-0.1.0-win-x64.zip`。普通玩家不需要编译源码。

只从本项目明确列出的地址下载，并核对相同版本的 SHA-256 校验值。目前没有已核验的国内镜像。

## 实际效果

[原版民族列表](https://github.com/Qihang-Li/aoede-chinese-font-fix/blob/main/docs/images/before-civilizations.png) · [修复后的民族列表](https://github.com/Qihang-Li/aoede-chinese-font-fix/blob/main/docs/images/after-civilizations.png) · [工具预览界面](https://github.com/Qihang-Li/aoede-chinese-font-fix/blob/main/docs/images/tool-preview.png) · [游戏内菜单](https://github.com/Qihang-Li/aoede-chinese-font-fix/blob/main/docs/images/after-game-menu.png)

以上均为实际截图。验证范围与尚未测试的情况见 [测试记录](docs/TESTING.md)。

## 首版支持范围

- Windows 64 位，.NET Framework 4.8，Steam 版第一代决定版。
- 以游戏 Build 97381 / Steam Build 12673858 对应的已知字体资源为基线；实际兼容判断使用文件哈希，而不是只比较版本号。
- 默认开源字体，以及用户提供的静态 TrueType `.ttf` 字体。自定义字体须包含所需汉字；可另外选择粗体文件。
- 不支持 TTC 字体集合、可变字体或 CFF 格式 OTF；不会把缺字偷偷替换成其他字体。
- 不适用于 AoE II DE、Return of Rome、AoE III DE 或 AoE IV。Microsoft Store、Steam Deck / Proton 未验证。

## 使用方法

1. 完整解压下载包，保留程序旁边的 `fonts`、`licenses` 等文件夹。
2. 关闭游戏，并等待 Steam 的游戏更新完成。
3. 打开 `AoEDE-Font-Fix.exe`。检查识别出的游戏目录，或选择包含 `AoEDE_s.exe` 的 `AoEDE` 文件夹。
4. 使用默认字体，或选择自己的常规、粗体 TTF 文件。自定义字体的授权由其许可决定，安装在电脑上不代表允许转换和再分发。
5. 检查资源并预览，确认字形后应用。第一次会保存原始字体备份。
6. 等待成功提示后启动游戏。

需要撤销时，关闭游戏，重新打开工具并使用恢复功能。保留游戏目录中的 `AoEDE-Chinese-Font-Fix-backup`；恢复时程序会检查备份和当前文件，遇到未知更新或其他修改会停止。

如果提示资源不支持，先确认选择了正确的游戏和目录。不要强行把旧文件覆盖到新版游戏，也不要删除备份来绕过检查。

## 修改范围与限制

工具只在本机生成、替换 16 份字体 DDS 中的汉字区域，目标范围为 U+3400–U+4DBF、U+4E00–U+9FFF、U+F900–U+FAFF。原有 FNT 排版信息、非汉字区域、游戏逻辑与存档不属于修改目标。

字体资源由游戏多个语言共用：即使以后切换到繁体中文或日语，同码汉字也会使用修改后的简体中文地区字形。切换语言前可恢复原版。不同字体的笔画粗细与原始字框不同，少量字形可能需要缩放；预览和游戏内检查很重要。

游戏更新、Steam 验证文件或其他字体 mod 可能覆盖修改。未知资源默认拒绝处理。不能保证未来所有版本、所有界面或所有字体都适用。

## 透明性与安全

- 完整源代码、构建脚本和测试代码公开；程序离线处理本机文件。
- 下载包不包含原游戏 DDS/FNT、游戏内附字体或 Windows 字体，也不包含任何用户备份。
- 先生成和检查候选文件，再安装；保存本机备份和状态记录，恢复时再次核对文件。
- 开源和校验值不能代替安全审计。校验值用于核对文件一致性；具备条件的用户可以自行审阅和构建。
- 本项目不提供反作弊、多人联机或新成就触发的兼容保证。

## 从源码构建

在 Windows 上使用系统 .NET Framework 4.x 的 C# 编译器运行 `build.ps1`。编译器支持 C# 5；不需要在线下载 NuGet 依赖。具体命令、构建记录和测试步骤见 [docs/BUILD.md](docs/BUILD.md)。

默认字体已包含在源码中；重新生成字体的方法见 `tools/prepare-fonts.py` 和字体来源说明。

## 反馈与维护

请通过 GitHub Issues 或 Steam 指南评论说明游戏版本、工具版本、操作步骤和错误提示。公开日志前去除个人用户名、私人路径及其他信息；不要上传整个游戏、备份或未经许可的字体文件。

新版本应同时更新 Releases、镜像链接和指南。发现游戏资源变化时，先验证适配，再发布支持该资源的新版本。

## 许可和声明

自写代码采用 [MIT License](LICENSE)，字体采用独立的 SIL OFL 1.1。游戏及商标属于各自权利人。本项目不是微软或 World's Edge 官方产品，也未获其背书。截图仅用于说明游戏内的实际显示效果，不属于本项目 MIT 许可内容。

This is an unofficial, offline font customization tool for Age of Empires: Definitive Edition (2019, Steam 1017900). It reads the user's existing game assets locally and does not redistribute them. Code: MIT. Bundled derivative fonts: SIL OFL 1.1.
