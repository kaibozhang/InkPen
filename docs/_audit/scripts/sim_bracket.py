"""行尾禁则（'(' 在页底末行触发）跨页复算。用 latin.zip，确保字形存在。只读。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(r"D:/Writer/InkPen/docs/_audit/tmp_a")))
from sim_layout import layout_horizontal

LAT = ["latin.zip"]
# latin 'A' advance? 先打一行 16 个 A，看一行能放几个
for n in (10, 16, 18):
    pages, _ = layout_horizontal("A" * n, LAT, mr=15)
    print("A*%d -> pages=%d page0 lines=%s n=%d" % (n, len(pages),
          sorted(set(g.line for g in pages[0])), len(pages[0])))

# 页底末行：15 个满行 + 最后一行末尾放 "(" 再跟一个字
full = "A" * 16
rows = [full for _ in range(15)] + [("A" * 15 + "(A")]
pages, miss = layout_horizontal("\n".join(rows), LAT)
print("pages=%d miss=%s" % (len(pages), miss))
for pi, pg in enumerate(pages):
    ys = [g.y for g in pg]
    print(" page %d n=%d y=[%.1f,%.1f] lines=%s" % (pi, len(pg), min(ys), max(ys),
          sorted(set(g.line for g in pg))))
for pi, pg in enumerate(pages):
    for g in pg:
        if g.ch == "(":
            print("  '(' page=%d line=%d x=%.1f y=%.1f ci=%d" % (g.page, g.line, g.x, g.y, g.ci))
    tail = sorted(pg, key=lambda g: g.ci)[-3:]
    print(" page %d tail: %s" % (pi, [(g.ch, g.line, round(g.y, 1)) for g in tail]))
