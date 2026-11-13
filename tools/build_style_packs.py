"""
从 chinese-hershey-font 的 STRK-*.json 生成可选的中文风格字库包。

  kaiti.zip  - 楷体（STRK-Kaiti）
  mingti.zip - 明体/宋体（STRK-Mingti）

坐标系与 chinese-hershey-font 一致：x∈[0,1] 向右、y∈[0,1] 向上，需翻转 y
并放大到 InkPen 的 1024 网格，否则字会上下颠倒。

运行：python tools/build_style_packs.py
"""
import io
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from glyph_format import GRID, write_font_zip  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "tools" / "_src" / "chinese-hershey-font-master" / "dist" / "json"
OUT = ROOT / "data" / "glyphs"

STYLES = [
    ("kaiti.zip", "STRK-Kaiti.json", "楷体"),
    ("mingti.zip", "STRK-Mingti.json", "明体"),
]


def load_strk(path):
    raw = json.load(io.open(path, encoding="utf-8"))
    out = {}
    for key, polylines in raw.items():
        cp = int(key[2:], 16)
        strokes = []
        for line in polylines:
            pts = [(float(p[0]) * GRID, (1.0 - float(p[1])) * GRID) for p in line]
            if len(pts) >= 2:
                strokes.append(pts)
        if strokes:
            out[cp] = (strokes, GRID)
    return out


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, src_name, label in STYLES:
        src = SRC / src_name
        if not src.is_file():
            print("[SKIP] 缺少源文件 " + str(src))
            continue
        g = load_strk(src)
        dst = OUT / name
        write_font_zip(dst,
                       g,
                       "chinese-hershey-font " + src_name + " (" + label + "); MIT. "
                       "Modified: y flip, scaled to 1024, packed into InkPen v1 binary.")
        print("%-12s %5d 字  %6.2f MB" % (name, len(g), dst.stat().st_size / 1048576.0))


if __name__ == "__main__":
    main()
