"""LayoutEngine.LayoutHorizontal 的忠实移植（jitter=0），用于复算边界场景。
只读字库 zip；不写项目文件。"""
import struct
import sys
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(r"D:/Writer/InkPen/tools")))
from glyph_format import GRID

G = Path(r"D:/Writer/InkPen/data/glyphs")
NO_LINE_START = "，。！？；：、）】》」』〕〉…·％‰.,!?;:)]}>"
NO_LINE_END = "（【《「『〔〈([{<"

_cache = {}


def load(pack):
    z = zipfile.ZipFile(G / pack)
    d = {}
    for n in z.namelist():
        if not n.isdigit():
            continue
        b = z.read(n)
        ns, adv = struct.unpack_from("<Hf", b, 0)
        off = 6
        for _ in range(ns):
            (npts,) = struct.unpack_from("<H", b, off)
            off += 2 + 8 * npts
        d[int(n)] = adv
    z.close()
    return d


def adv_of(packs, ch):
    for p in packs:
        if p not in _cache:
            _cache[p] = load(p)
        d = _cache[p]
        if ord(ch) in d:
            return d[ord(ch)]
    return None


class Glyph:
    __slots__ = ("ch", "x", "y", "size", "adv", "page", "line", "ci")

    def __init__(self, ch, x, y, size, adv, page, line, ci):
        self.ch, self.x, self.y, self.size, self.adv = ch, x, y, size, adv
        self.page, self.line, self.ci = page, line, ci


def layout_horizontal(text, packs, paperW=210.0, paperH=297.0,
                      mL=15.0, mR=15.0, mT=15.0, mB=15.0,
                      font=10.0, chargap=1.0, linegap=6.0, indent=0.0,
                      justify=False, center=False):
    usableW = paperW - mL - mR
    lineH = font + linegap
    if lineH < 1:
        lineH = font + 1
    pages = [[]]
    x = mL + indent
    y = paperH - mT - font
    bottom = mB + font
    lineIndex = 0
    charIndex = 0
    line = []
    missing = []

    def flush(page_idx, line, lineIndex):
        if not line:
            return
        if justify or center:
            used = sum(p.adv for p in line)
            extra = usableW - used
            if center and extra > 0:
                for p in line:
                    p.x += extra / 2.0
            elif justify and extra > 0 and len(line) > 1:
                step = extra / (len(line) - 1)
                for i in range(1, len(line)):
                    line[i].x += step * i
        for p in line:
            p.line = lineIndex
            p.page = page_idx
            pages[page_idx].append(p)

    text = text.replace("\r\n", "\n").replace("\r", "\n").replace("\t", " ")
    for ch in text:
        if ch == "\n":
            flush(len(pages) - 1, line, lineIndex)
            line = []
            lineIndex += 1
            x = mL
            y -= lineH
            if y < bottom:
                pages.append([])
                y = paperH - mT - font
                lineIndex = 0
            continue
        if ch == " ":
            x += font * 0.5
            continue
        a = adv_of(packs, ch)
        if a is None:
            if ch not in missing:
                missing.append(ch)
            x += font
            continue
        adv = max(a, 1) / 1024.0 * font + chargap
        if x + adv > mL + usableW and line:
            if ch in NO_LINE_START:
                pass
            elif line[-1].ch in NO_LINE_END:
                moved = line.pop()
                flush(len(pages) - 1, line, lineIndex)
                line = []
                line.append(moved)
                moved.x = mL
                moved.y -= lineH
                lineIndex += 1
                moved.line = lineIndex
                x = mL + moved.adv
                y -= lineH
            else:
                flush(len(pages) - 1, line, lineIndex)
                line = []
                lineIndex += 1
                x = mL
                y -= lineH
            if y < bottom:
                pages.append([])
                y = paperH - mT - font
                lineIndex = 0
        g = Glyph(ch, x, y, font, adv, len(pages) - 1, lineIndex, charIndex)
        charIndex += 1
        line.append(g)
        x += adv
    flush(len(pages) - 1, line, lineIndex)
    return pages, missing


def report(name, pages, missing=None):
    print("### %s -> pages=%d glyphs=%d" % (name, len(pages), sum(len(p) for p in pages)))
    for pi, pg in enumerate(pages):
        if not pg:
            print("  page %d: (empty)" % pi)
            continue
        xs = [g.x for g in pg]
        ys = [g.y for g in pg]
        print("  page %d: n=%d x=[%.2f,%.2f] y=[%.2f,%.2f] lines=%s"
              % (pi, len(pg), min(xs), max(xs), min(ys), max(ys),
                 sorted(set(g.line for g in pg))))
    if missing:
        print("  missing:", missing)


CJK = ["mmah.zip"]

# 1) Justify + 首行缩进：右边界是否被突破
p, _ = layout_horizontal("我" * 15, CJK, indent=8.0, justify=True)
g = p[0][-1]
print("1) justify+indent: last char x=%.3f right_edge=%.3f limit=%.3f overflow=%.3f"
      % (g.x, g.x + g.size, 210 - 15, g.x + g.size - (210 - 15)))
p, _ = layout_horizontal("我" * 15, CJK, indent=8.0, justify=False)
print("   no-justify last x=%.3f" % p[0][-1].x)

# 2) 每页行数
lines = 20
p, _ = layout_horizontal("\n".join(["我" * 5] * lines), CJK)
report("2) 20 lines", p)
print("   rows in page0:", len(set(x.line for x in p[0])))

# 3) 行尾禁则（开引号）跨页：构造让 “（” 落在页底最后一行
#    每页 16 行 -> 第 16 行末尾放 “（”，后面还有字触发换行
rows = []
for i in range(15):
    rows.append("我" * 16)          # 16 CJK 占满一行 (16*11=176 < 180)
rows.append("我" * 14 + "（我")
p, _ = layout_horizontal("\n".join(rows), CJK)
report("3) bracket at page bottom", p)
for pi, pg in enumerate(p):
    for x in pg:
        if x.ch == "（":
            print("   '（' at page=%d line=%d x=%.2f y=%.2f" % (x.page, x.line, x.x, x.y))

# 4) 行首空格导致越界
p, _ = layout_horizontal(" " * 60 + "我", CJK)
report("4) leading spaces", p)

# 5) 制表符
p, _ = layout_horizontal("我\t我", CJK)
print("5) tab: x=%s" % [round(x.x, 2) for x in p[0]])
