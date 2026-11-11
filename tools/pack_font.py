"""
InkPen 字库双态工具：开发态每字 JSON <-> 分发态二进制 ZIP。

  python pack_font.py export            # 从 data/glyphs/*.zip 导出人可读 JSON 到 data/glyphs/dev/<font>/
  python pack_font.py pack <font>       # 从 dev/<font>/*.json 打包回 data/glyphs/<font>.zip
  python pack_font.py export --font local.zip --chars 写字机   # 只导出指定字，便于手绘补录/人工修正

开发态 JSON（开放格式，可 diff、可手绘补录、缺字补录的目标格式）：
  {
    "cp": 27721, "ch": "写", "advance": 1024,
    "strokes": [{"pen": "down", "pts": [[x, y], ...]}, ...],
    "source": "local.zip", "license": "private"
  }

分发态：InkPen Glyph v1 二进制 ZIP（见 glyph_format.py / data/glyphs/schema.json）。
单向生成链：dev JSON --pack--> ZIP；export 仅用于初始化 dev 目录或抽查。
"""
import io
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from glyph_format import GRID, pack_glyph, read_font_zip  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
G = ROOT / "data" / "glyphs"
DEV = G / "dev"


def export_font(font, only_chars=None):
    src = G / font
    if not src.is_file():
        print("missing", src)
        return 0
    dst = DEV / font[:-4]
    dst.mkdir(parents=True, exist_ok=True)
    data = read_font_zip(src)
    n = 0
    for cp, (strokes, adv) in data.items():
        ch = chr(cp)
        if only_chars and ch not in only_chars:
            continue
        obj = {
            "cp": cp, "ch": ch, "advance": adv,
            "strokes": [{"pen": "down", "pts": [[round(x, 3), round(y, 3)] for x, y in s]}
                        for s in strokes],
            "source": font,
            "license": "private" if font == "local.zip" else "see NOTICE.md"
        }
        io.open(dst / (str(cp) + ".json"), "w", encoding="utf-8").write(
            json.dumps(obj, ensure_ascii=False, indent=1))
        n += 1
    print("exported", font, "->", dst, n, "glyphs")
    return n


def pack_font(font):
    src = DEV / font
    if not src.is_dir():
        print("missing dev dir", src)
        return 0
    out = G / (font + ".zip")
    glyphs = {}
    for f in src.iterdir():
        if not f.name.endswith(".json"):
            continue
        o = json.load(io.open(f, encoding="utf-8"))
        cp = int(o["cp"])
        strokes = [[(float(p[0]), float(p[1])) for p in st["pts"]]
                   for st in o.get("strokes", []) if st.get("pen", "down") == "down"]
        glyphs[cp] = (strokes, float(o.get("advance", GRID)))
    import zipfile
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for cp in sorted(glyphs):
            st, adv = glyphs[cp]
            z.writestr(str(cp), pack_glyph(st, adv))
        z.writestr("_SOURCE.txt",
                   "Packed from InkPen dev JSON by tools/pack_font.py; see NOTICE.md and schema.json.")
    print("packed", src, "->", out, len(glyphs), "glyphs")
    return len(glyphs)


def main(argv):
    if len(argv) < 2 or argv[1] == "export":
        only = None
        font = None
        if "--font" in argv:
            font = argv[argv.index("--font") + 1]
        if "--chars" in argv:
            only = set(argv[argv.index("--chars") + 1])
        fonts = [font] if font else [f for f in os.listdir(G) if f.endswith(".zip")]
        total = 0
        for f in fonts:
            total += export_font(f, only)
        print("total exported", total)
        return 0
    if argv[1] == "pack" and len(argv) > 2:
        pack_font(argv[2])
        return 0
    print(__doc__)
    return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
