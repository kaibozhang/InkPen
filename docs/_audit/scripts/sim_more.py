"""复算：行尾禁则跨页、格线与字形对齐、格线重复段、页容量。只读。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(r"D:/Writer/InkPen/docs/_audit/tmp_a")))
from sim_layout import layout_horizontal

CJK = ["mmah.zip"]

# ---- 3') 让 “（” 正好是第 16 行（页底末行）的第 17 个字，触发 wrap + NO_LINE_END
rows = ["我" * 16 for _ in range(15)]      # 15 个满行
rows.append("我" * 16 + "（我")            # 第 16 行：第 17 字是 “（”，触发换行
pages, _ = layout_horizontal("\n".join(rows), CJK)
print("pages=%d" % len(pages))
for pi, pg in enumerate(pages):
    print(" page %d: n=%d y=[%.1f,%.1f]" % (pi, len(pg), min(g.y for g in pg), max(g.y for g in pg)))
    for g in pg:
        if g.ch in "（我" and g.line <= 1:
            pass
br = [(g.page, g.line, round(g.x, 1), round(g.y, 1)) for pg in pages for g in pg if g.ch == "（"]
print(" '(' placements:", br)
# 该行（第 16 行, line=15）原本在 page0；bracket 之后的字去哪了
for pi, pg in enumerate(pages):
    last = sorted(pg, key=lambda g: g.ci)[-3:]
    print(" page %d tail:" % pi, [(g.ch, g.line, round(g.y, 1), g.ci) for g in last])

# ---- 格线（Grid）与首行缩进对齐
paperW, paperH = 210.0, 297.0
mL, mR, mT, mB = 15.0, 15.0, 15.0, 15.0
font, chargap, linegap, indent = 10.0, 1.0, 6.0, 8.0
pages, _ = layout_horizontal("我" * 40, CJK, mL=mL, mR=mR, mT=mT, mB=mB,
                             font=font, chargap=chargap, linegap=linegap, indent=indent)
cell = font + chargap
mR_abs = paperW - mR
cells = []
x = mL
while x + cell <= mR_abs + 0.01:
    cells.append((x, x + cell))
    x += cell
print("cells=%d first=%s last=%s" % (len(cells), cells[0], cells[-1]))
gs = [g for g in pages[0] if g.line == 0]
print("line0 glyph x:", [round(g.x, 1) for g in gs[:6]])
outside = [g for g in gs if not any(a - 1e-9 <= g.x <= b for a, b in cells)]
print("line0 glyphs not on any cell left edge: %d/%d  e.g. x=%.1f" % (len(outside), len(gs), gs[0].x))

# ---- 格线重复段统计（Grid）：相邻格共享竖边被画两次
segs = []
for (a, b) in cells:
    segs.append((round(a, 6), "L"))
    segs.append((round(b, 6), "R"))
from collections import Counter
c = Counter(s for s, _ in segs)
print("vertical cell edges: total=%d distinct=%d duplicated=%d"
      % (len(segs), len(c), sum(v - 1 for v in c.values() if v > 1)))

# ---- 页容量：16 行/页 vs 理论 17 行/页
usableH = paperH - mT - mB
lineH = font + linegap
theoretical = int((usableH - font) // lineH) + 1
current = int((paperH - mT - font - (mB + font)) // lineH) + 1
print("usableH=%.0f lineH=%.0f theoretical_rows=%d current_rows=%d" % (usableH, lineH, theoretical, current))
