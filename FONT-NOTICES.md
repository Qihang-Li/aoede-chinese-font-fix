# Bundled font / 内置字体说明

The bundled **Aoe CJK Sans** fonts are renamed static derivatives of **Adobe Source Han Sans CN 2.005** (思源黑体简体中文区域字形). The regular and bold files use weight 400 and 700. They retain the upstream glyph outlines; the build only freezes the weight axis, updates naming/style metadata and preserves a fixed timestamp.

内置字体以 Adobe 思源黑体的简体中文区域版本为基础。为了兼容 Windows 字体绘制接口，将可变字体转换成 Regular（400）和 Bold（700）两个静态 TrueType 文件。字形设计没有重新绘制。为遵守上游保留字体名 `Source` 的要求，衍生字体的显示名称改为 **Aoe CJK Sans**。这不表示 Adobe 或游戏发行商认可本项目。

## License / 许可

- Copyright 2014–2025 Adobe. Upstream Reserved Font Name: `Source`.
- Both bundled TTF files and font-software derivatives remain under the **SIL Open Font License 1.1**, not the application's code license.
- The complete upstream copyright notice and license are preserved in [`licenses/SourceHanSans-OFL.txt`](licenses/SourceHanSans-OFL.txt).
- Keep that notice and license when redistributing these fonts or their font-software derivatives. Do not sell the font files on their own. Follow the license's naming and attribution conditions.
- 本项目不包含微软雅黑、其他 Windows 专有字体或原游戏字体贴图。用户选择其他字体时，应自行确认其许可允许对应的字库转换用途；电脑上已安装某字体并不代表其允许此类转换或再分发。
- This license covers the bundled font software. It does not grant any rights to the game or to unrelated materials.

## Pinned source / 固定来源

- Official repository: <https://github.com/adobe-fonts/source-han-sans>
- Commit: `a4f7cf94edfb9d7ffbdfc4841de276358bd7e0f2`
- Font: <https://raw.githubusercontent.com/adobe-fonts/source-han-sans/a4f7cf94edfb9d7ffbdfc4841de276358bd7e0f2/Variable/TTF/Subset/SourceHanSansCN-VF.ttf>
- Font SHA-256: `25a01e41b5cc99893eb35a6cd2cc7611841dc19eb03cbaf7f0c1de8210f2ba0b`
- License: <https://raw.githubusercontent.com/adobe-fonts/source-han-sans/a4f7cf94edfb9d7ffbdfc4841de276358bd7e0f2/LICENSE.txt>
- License SHA-256: `fcac737e761ec63dbfbdce11030a1780161920d80315edba9c8beff1c2bac5a2`

The output hashes, sizes, source and transformation details are recorded in [`fonts/provenance.json`](fonts/provenance.json). Both fonts cover all 3,688 distinct Han characters targeted in the supported game font resources. Their full Unicode cmap contains 30,926 code points. The bundled font files do not require a system-wide font installation.

## Rebuilding / 重新生成

Building these font files is optional: ordinary users use the bundled files. Developers can reproduce them with **Python 3.12** and **fonttools 4.60.1**:

```text
python -m pip install fonttools==4.60.1
python tools/prepare-fonts.py
```

The build script downloads only the two pinned Adobe resources and validates their SHA-256 before processing. An offline build can supply already-downloaded inputs:

```text
python tools/prepare-fonts.py --source SourceHanSansCN-VF.ttf --license-source LICENSE.txt
```

This developer script does not install fonts or change game files. Python and fontTools are build tools only; the player-facing application does not need them.
