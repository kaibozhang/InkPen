"""
生成 InkPen 主字库：
  data/glyphs/mmah.zip    - Make Me A Hanzi medians（带正确笔顺，Arphic PL）
  data/glyphs/fallback.zip- chinese-hershey-font 补缺字（MIT）
  data/glyphs/latin.zip   - Hershey Futural 拉丁/数字/标点（自由使用+致谢）
  data/glyphs/latin_script.zip - Hershey Scripts 手写连笔风格（可选切换）
  data/glyphs/pinyin.zip  - 自造拼音声调字符（基字母 + 声调笔画）
  data/glyphs/schema.json - 格式与来源说明

运行：Python 3.12 绝对路径执行，工作目录任意。
"""
import io
import json
import os
import re
import sys
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from glyph_format import GRID, write_font_zip  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
TOOLS = ROOT / "tools"
SRC = TOOLS / "_src"
OUT = ROOT / "data" / "glyphs"
UPSTREAM = ROOT / "src" / "Upstream" / "GRBL-Plotter"

MMah_DIR = SRC / "hanzi-writer-data-master" / "data"
CHF_JSON = SRC / "chinese-hershey-font-master" / "dist" / "json" / "STRK-Kaiti.json"
HERSHEY_CS = UPSTREAM / "GCodeCreation__CreateFromForm__GCodeFromFontHershey.cs"

# MMAH 网格 y 范围 [-124, 900]，整体上移 124 到 [0, 1024]
MMAH_Y_OFFSET = 124.0
# medians 点列较稀疏，做轻度 Catmull-Rom 插值让笔画圆润（每两点间插 SUBDIV 段）
SUBDIV = 2


def catmull_rom(points, subdiv=SUBDIV):
    if len(points) < 3 or subdiv <= 1:
        return list(points)
    out = []
    n = len(points)
    for i in range(n - 1):
        p0 = points[max(0, i - 1)]
        p1 = points[i]
        p2 = points[i + 1]
        p3 = points[min(n - 1, i + 2)]
        for k in range(subdiv):
            t = k / subdiv
            t2, t3 = t * t, t * t * t
            x = 0.5 * ((2 * p1[0]) + (-p0[0] + p2[0]) * t
                       + (2*p0[0] - 5*p1[0] + 4*p2[0] - p3[0]) * t2
                       + (-p0[0] + 3*p1[0] - 3*p2[0] + p3[0]) * t3)
            y = 0.5 * ((2 * p1[1]) + (-p0[1] + p2[1]) * t
                       + (2*p0[1] - 5*p1[1] + 4*p2[1] - p3[1]) * t2
                       + (-p0[1] + 3*p1[1] - 3*p2[1] + p3[1]) * t3)
            out.append((x, y))
    out.append(points[-1])
    return out


def load_mmah():
    out = {}
    for name in os.listdir(MMah_DIR):
        if not name.endswith(".json") or name == "all.json":
            continue
        ch = name[:-5]
        if len(ch) != 1:
            continue
        d = json.load(io.open(MMah_DIR / name, encoding="utf-8"))
        strokes = []
        for median in d.get("medians", []):
            pts = [(float(p[0]), float(p[1]) + MMAH_Y_OFFSET) for p in median]
            if len(pts) < 2:
                continue
            strokes.append(catmull_rom(pts))
        if strokes:
            out[ord(ch)] = (strokes, GRID)
    return out


def load_chf():
    raw = json.load(io.open(CHF_JSON, encoding="utf-8"))
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


def extract_hershey_table(name):
    """从 GRBL-Plotter 的 GCodeFromFontHershey.cs 提取 string[] 字体表。"""
    s = io.open(HERSHEY_CS, encoding="utf-8", errors="replace").read()
    m = re.search(r"string\[\]\s+" + name + r"\s*=\s*new\s+string\[\]\s*\{", s)
    if not m:
        raise RuntimeError("font table not found: " + name)
    start = m.end()
    end = s.find("};", start)
    body = s[start:end]
    return re.findall(r'"((?:[^"\\]|\\.)*)"', body)


def parse_hershey_glyph(glyph):
    """'left right M x y L x y ...' -> (strokes, advance_raw)"""
    t = glyph.split()
    if len(t) < 2:
        return [], 0
    try:
        left, right = float(t[0]), float(t[1])
    except ValueError:
        return [], 0
    strokes, cur = [], None
    i = 2
    while i + 2 < len(t):
        cmd = t[i]
        try:
            x, y = float(t[i + 1]), float(t[i + 2])
        except ValueError:
            break
        if cmd == "M":
            cur = [(x, y)]
            strokes.append(cur)
        elif cmd == "L" and cur is not None:
            cur.append((x, y))
        i += 3
    return [s for s in strokes if len(s) >= 2], (right - left)


def load_hershey_latin(table_name):
    table = extract_hershey_table(table_name)
    scale = 717.0 / 21.0
    baseline = 124.0
    out = {}
    for idx, glyph in enumerate(table):
        cp = 32 + idx  # Hershey 表从空格开始
        if cp > 0x7E:
            break
        strokes, adv_raw = parse_hershey_glyph(glyph)
        if not strokes:
            out[cp] = ([], max(adv_raw * scale, 120.0))
            continue
        # Y 翻转：Hershey y 向下，9 为基线
        conv = [[(x * scale, baseline + (9.0 - y) * scale) for x, y in s] for s in strokes]
        xs = [p[0] for s in conv for p in s]
        if xs:
            shift = (GRID - (max(xs) - min(xs))) / 2.0 - min(xs)
            conv = [[(x + shift, y) for x, y in s] for s in conv]
        out[cp] = (conv, max(adv_raw * scale, 120.0))
    return out


# 常见中文全角标点：1024 网格、Y 向上、按汉字字身比例绘制
CJK_PUNCT = {
    "，": [[(300, 180), (330, 120), (360, 90)]],
    "。": [[(300, 150), (270, 190), (300, 230), (360, 230), (390, 190), (360, 150), (300, 150)]],
    "、": [[(320, 200), (380, 140)]],
    "；": [[(320, 700), (320, 660)], [(320, 220), (350, 160), (380, 130)]],
    "：": [[(340, 700), (340, 660)], [(340, 250), (340, 210)]],
    "！": [[(340, 850), (340, 400)], [(340, 200), (340, 170)]],
    "？": [[(250, 780), (290, 860), (360, 860), (410, 790), (390, 650), (340, 520)], [(340, 200), (340, 170)]],
    "“": [[(260, 780), (300, 700)], [(370, 780), (410, 700)]],
    "”": [[(260, 860), (300, 780)], [(370, 860), (410, 780)]],
    "‘": [[(330, 800), (360, 720)]],
    "’": [[(330, 860), (360, 780)]],
    "（": [[(430, 880), (350, 700), (320, 510), (350, 300), (430, 120)]],
    "）": [[(590, 880), (670, 700), (700, 510), (670, 300), (590, 120)]],
    "《": [[(430, 780), (300, 510), (430, 240)]],
    "》": [[(590, 780), (720, 510), (590, 240)]],
    "【": [[(430, 900), (320, 900), (320, 120), (430, 120)]],
    "】": [[(590, 900), (700, 900), (700, 120), (590, 120)]],
    "—": [[(120, 510), (900, 510)]],
    "…": [[(200, 200), (220, 200)], [(490, 200), (510, 200)], [(780, 200), (800, 200)]],
    "·": [[(490, 510), (530, 510)]],
}

# 拼音声调：基字母 + 声调笔画（放在上格 y≈880~960）
TONE_MARKS = {
    "\u0304": [[(390, 920), (630, 920)]],                                  # 一声 平
    "\u0301": [[(390, 860), (630, 960)]],                                  # 二声 扬
    "\u030c": [[(390, 960), (510, 870), (630, 960)]],                       # 三声 拐
    "\u0300": [[(390, 960), (630, 860)]],                                  # 四声 降
}


def build_pinyin(latin):
    import unicodedata
    out = {}
    for ch in "āáǎàēéěèīíǐìōóǒòūúǔùǖǘǚǜüĀÁǍÀĒÉĚÈĪÍǏÌŌÓǑÒŪÚǓÙ":
        base = unicodedata.normalize("NFD", ch)[0]
        tone = unicodedata.normalize("NFD", ch)[1:]
        if not tone or ord(base) not in latin:
            continue
        strokes = [list(s) for s in latin[ord(base)][0]]
        mark = TONE_MARKS.get(tone[0])
        if mark:
            strokes.extend([list(s) for s in mark])
        out[ord(ch)] = (strokes, latin[ord(base)][1])
    return out


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    print("[1/5] MMAH medians ...")
    mmah = load_mmah()
    write_font_zip(OUT / "mmah.zip", mmah,
                   "Make Me A Hanzi / hanzi-writer-data medians; Arphic Public License. "
                   "Modified: y+124, Catmull-Rom smoothing, packed into InkPen v1 binary.")
    print("      mmah:", len(mmah), "glyphs")

    print("[2/5] chinese-hershey-font fallback ...")
    chf = load_chf()
    # 只保留 MMAH 缺失的字，减小体积
    fb = {cp: v for cp, v in chf.items() if cp not in mmah}
    write_font_zip(OUT / "fallback.zip", fb,
                   "chinese-hershey-font STRK-Kaiti.json; MIT. "
                   "Modified: y flip, scaled to 1024, packed into InkPen v1 binary.")
    print("      chf total:", len(chf), "fallback kept:", len(fb))

    print("[3/5] Hershey Latin ...")
    latin = load_hershey_latin("Futural")
    latin_script = load_hershey_latin("Scripts")
    for ch, punct_strokes in CJK_PUNCT.items():
        latin.setdefault(ord(ch), (punct_strokes, GRID))
        latin_script.setdefault(ord(ch), (punct_strokes, GRID))
    write_font_zip(OUT / "latin.zip", latin,
                   "Hershey Futural via GRBL-Plotter data/fonts; ACKNOWLEDGEMENT required. "
                   "Modified: M/L parsed to strokes, scaled, packed into InkPen v1 binary.")
    write_font_zip(OUT / "latin_script.zip", latin_script,
                   "Hershey Scripts via GRBL-Plotter data/fonts; ACKNOWLEDGEMENT required.")
    print("      latin:", len(latin), "script:", len(latin_script))

    print("[4/5] Pinyin tone marks ...")
    py = build_pinyin(latin)
    write_font_zip(OUT / "pinyin.zip", py,
                   "Self-authored: Hershey base letter + hand-authored tone stroke.")
    print("      pinyin:", len(py))

    print("[5/5] Schema + coverage ...")
    schema = {
        "format": "InkPen Glyph v1",
        "grid": 1024,
        "origin": "bottom-left, Y up",
        "container": "ZIP; entry name = decimal Unicode codepoint",
        "entry": "uint16 strokeCount; float32 advance; per stroke: uint16 pointCount, then pointCount * (float32 x, float32 y); little-endian",
        "semantics": "each stroke is one continuous pen-down polyline; stroke boundary = pen up",
        "fonts": {
            "mmah.zip": {"source": "hanzi-writer-data / Make Me A Hanzi", "license": "Arphic Public License", "count": len(mmah), "role": "primary, correct stroke order"},
            "fallback.zip": {"source": "chinese-hershey-font STRK-Kaiti", "license": "MIT", "count": len(fb), "role": "missing-character fallback, no stroke order"},
            "latin.zip": {"source": "Hershey Futural (GRBL-Plotter data/fonts)", "license": "free use + acknowledgement", "count": len(latin), "role": "Latin/digits/ASCII/CJK punctuation"},
            "latin_script.zip": {"source": "Hershey Scripts", "license": "free use + acknowledgement", "count": len(latin_script), "role": "cursive Latin option"},
            "pinyin.zip": {"source": "self-authored", "license": "own work", "count": len(py), "role": "precomposed pinyin tone vowels"},
            "local.zip": {"source": "local .gfont import + hand-drawn", "license": "private, not distributed", "role": "user handwriting style"}
        }
    }
    io.open(OUT / "schema.json", "w", encoding="utf-8").write(json.dumps(schema, ensure_ascii=False, indent=2))
    cover = set(mmah) | set(chf) | set(latin)
    print("      union coverage:", len(cover), "codepoints")
    print("      output:", OUT)


if __name__ == "__main__":
    main()
