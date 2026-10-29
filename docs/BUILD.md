# 构建与发布

## 工具链

- Windows x64，.NET Framework 4.8。
- 使用 Windows 自带 Framework64/v4.0.30319/csc.exe，C# 5。
- 仅引用 .NET Framework 的 System、System.Core、System.Drawing、System.Windows.Forms、System.Runtime.Serialization。
- 玩家无需安装 Python、PowerShell 7、开发 SDK 或联网下载组件。

在仓库根目录执行：

```powershell
.\build.ps1
```

输出位于 `dist/AoEDE-Chinese-Font-Fix-0.1.0`。将该目录完整压缩并发布；不要只分发 EXE。首次编译前应审阅源码和脚本。构建脚本不会修改游戏。

`.github/workflows/build.yml` 提供公开的 Windows 构建流程；工作流只有读取仓库的权限，不自动部署。发布记录应明确对应的源码版本。当前没有承诺不同 Windows 工具链生成的 EXE 逐字节一致；源码可审阅和自行构建，并不等于已经通过第三方安全审计。

## 默认字体

两个静态 TTF 已包含在 `fonts/` 中。重建字体需要独立的 Python / fontTools 开发环境；步骤见 `FONT-NOTICES.md`。普通编译与运行不需要重新生成字体。

## 命令行

不带参数启动图形界面。自动化可用：

```text
AoEDE-Font-Fix.exe --inspect --game "D:\SteamLibrary\steamapps\common\AoEDE" --log inspect.txt
AoEDE-Font-Fix.exe --preview --game "D:\SteamLibrary\steamapps\common\AoEDE" --out preview.png --log preview.txt
AoEDE-Font-Fix.exe --apply --game "D:\SteamLibrary\steamapps\common\AoEDE" --log apply.txt
AoEDE-Font-Fix.exe --restore --game "D:\SteamLibrary\steamapps\common\AoEDE" --log restore.txt
```

自选字体增加 `--regular font.ttf` 和可选的 `--bold bold.ttf`。这是图形子系统 EXE，PowerShell 自动化应使用 `Start-Process -WindowStyle Hidden -Wait -PassThru` 等待完成，再查看退出码及 `--log` 文件。

退出码：0 成功，1 操作失败，2 参数错误，3 当前资源不支持。应用与恢复会写入游戏字体目录，仅在确认目标目录和备份状态后执行。预览文件应保存到游戏目录之外。

## 打包原则

1. 使用清洁的构建输出目录。
2. 运行测试并完成游戏内目视检查。
3. 检查包内只有程序、配置、开放许可字体、说明与许可证，没有原游戏资源、个人路径、备份或开发日志。
4. 生成下载包 SHA-256，与源码版本、工具版本及测试记录一起发布。
5. GitHub 与国内镜像使用同一份 ZIP；下载后再次核对哈希。
6. 更新指南中的版本、支持范围、链接和已知问题。
