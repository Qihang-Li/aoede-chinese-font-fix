#!/usr/bin/env python3
"""Build the two bundled OFL static TTF fonts from a pinned Adobe source.

Build-only dependency: Python 3.12 and fonttools==4.60.1.
Usage: python tools/prepare-fonts.py --source /path/SourceHanSansCN-VF.ttf
The source is downloaded from Adobe only when --source is omitted.
This script does not install fonts or modify a game installation.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import tempfile
import urllib.request

import fontTools
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

COMMIT = "a4f7cf94edfb9d7ffbdfc4841de276358bd7e0f2"
SOURCE_URL = f"https://raw.githubusercontent.com/adobe-fonts/source-han-sans/{COMMIT}/Variable/TTF/Subset/SourceHanSansCN-VF.ttf"
LICENSE_URL = f"https://raw.githubusercontent.com/adobe-fonts/source-han-sans/{COMMIT}/LICENSE.txt"
SOURCE_SHA256 = "25a01e41b5cc99893eb35a6cd2cc7611841dc19eb03cbaf7f0c1de8210f2ba0b"
LICENSE_SHA256 = "fcac737e761ec63dbfbdce11030a1780161920d80315edba9c8beff1c2bac5a2"
FAMILY = "Aoe CJK Sans"
FIXED_TIMESTAMP = 3833091615  # Preserve the upstream head.modified timestamp.


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def download(url: str, target: Path, expected: str) -> None:
    request = urllib.request.Request(url, headers={"User-Agent": "AoEDE-Chinese-Font-Fix-font-build"})
    with urllib.request.urlopen(request, timeout=120) as response:
        target.write_bytes(response.read())
    if sha256(target) != expected:
        raise RuntimeError(f"Unexpected SHA-256 for {target.name}; aborting")


def rename(font: TTFont, style: str, weight: int) -> None:
    # Replace every localized family/style identifier, including optional
    # typographic, WWS and variable-font PostScript naming fields. Preserve the
    # original copyright, authorship, license and source attribution metadata.
    names = font["name"]
    replace_ids = {1, 2, 3, 4, 5, 6, 16, 17, 18, 21, 22, 25}
    names.names = [record for record in names.names if record.nameID not in replace_ids]
    fields = {
        1: FAMILY,
        2: style,
        3: f"2.005-AoeCjkSans-0.1.0-{style}",
        4: f"{FAMILY} {style}",
        5: "Version 2.005; Aoe CJK Sans static build 0.1.0",
        6: f"AoeCjkSans-{style}",
        16: FAMILY,
        17: style,
        18: f"{FAMILY} {style}",
        21: FAMILY,
        22: style,
    }
    for name_id, value in fields.items():
        names.setName(value, name_id, 3, 1, 0x409)
        names.setName(value, name_id, 1, 0, 0)
    names.setName(
        "Static Chinese font derived from Adobe Source Han Sans CN 2.005. "
        "Instantiated at wght=" + str(weight) + "; renamed to comply with the OFL Reserved Font Name. "
        "Not endorsed by Adobe. The outlines have not been redesigned.",
        10, 3, 1, 0x409,
    )
    os2 = font["OS/2"]
    os2.usWeightClass = weight
    os2.fsSelection &= ~((1 << 0) | (1 << 5) | (1 << 6))
    os2.fsSelection |= (1 << 5) if style == "Bold" else (1 << 6)
    font["head"].macStyle &= ~3
    if style == "Bold":
        font["head"].macStyle |= 1
    font["head"].modified = FIXED_TIMESTAMP
    font.recalcTimestamp = False


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, help="Previously downloaded pinned upstream font")
    parser.add_argument("--license-source", type=Path, help="Previously downloaded pinned upstream LICENSE.txt")
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    if fontTools.__version__ != "4.60.1":
        raise RuntimeError("Use fonttools==4.60.1 for this reproducible build")
    project = args.project.resolve()
    fonts_dir, licenses_dir = project / "fonts", project / "licenses"
    fonts_dir.mkdir(parents=True, exist_ok=True)
    licenses_dir.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="aoe-cjk-font-build-") as scratch:
        source = args.source or Path(scratch) / "SourceHanSansCN-VF.ttf"
        if args.source is None:
            download(SOURCE_URL, source, SOURCE_SHA256)
        if sha256(source) != SOURCE_SHA256:
            raise RuntimeError("Source font is not the pinned Adobe font")
        license_path = licenses_dir / "SourceHanSans-OFL.txt"
        if args.license_source:
            if sha256(args.license_source) != LICENSE_SHA256:
                raise RuntimeError("License is not the pinned Adobe license")
            license_path.write_bytes(args.license_source.read_bytes())
        else:
            download(LICENSE_URL, license_path, LICENSE_SHA256)
        output_records = []
        for style, weight in (("Regular", 400), ("Bold", 700)):
            font = TTFont(source, recalcTimestamp=False)
            font = instantiateVariableFont(font, {"wght": weight}, inplace=True, optimize=True)
            rename(font, style, weight)
            if "glyf" not in font or "fvar" in font or "gvar" in font:
                raise RuntimeError("Expected a fully static TrueType glyf font")
            out = fonts_dir / f"AoeCjkSans-{style}.ttf"
            font.save(out, reorderTables=True)
            output_records.append({
                "file": out.name, "sha256": sha256(out), "bytes": out.stat().st_size,
                "family": FAMILY, "style": style, "weight": weight,
                "unicode_cmap_count": len(font.getBestCmap()),
            })
            font.close()
            print(f"Built {out.name}: {output_records[-1]['sha256']}")
        manifest = {
            "upstream": "Adobe Source Han Sans CN variable TrueType 2.005",
            "source_commit": COMMIT, "source_url": SOURCE_URL, "source_sha256": SOURCE_SHA256,
            "license_url": LICENSE_URL, "license_sha256": LICENSE_SHA256,
            "license": "SIL Open Font License 1.1", "reserved_upstream_font_name": "Source",
            "derivative_family": FAMILY, "fonttools_version": fontTools.__version__,
            "transform": "Instantiate weight 400/700; rename family/style IDs; normalize regular/bold style flags; preserve upstream timestamp. No outlines redrawn.",
            "files": output_records,
        }
        (fonts_dir / "provenance.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
