"""维度 B 的算例复现：在 sim_gcode.py（已与 8 份真实 .nc 逐字节对齐）上跑边界场景。"""
import math
import re
import sim_gcode as S

FONTS = S.Fonts(False, "latin.zip")


def base_cfg(**kw):
    c = S.Cfg()
    for k, v in kw.items():
        setattr(c, k, v)
    return c


def tilted_map(cols=3, rows=3, z_at_right=6.0, margin=10.0, use=True):
    hm = {"cols": cols, "rows": rows,
          "minx": margin, "miny": margin,
          "maxx": 210.0 - margin, "maxy": 297.0 - margin,
          "z": [0.0] * (cols * rows)}
    for r in range(rows):
        for c in range(cols):
            hm["z"][r * cols + c] = z_at_right * c / (cols - 1)
    return hm if use else None


def z_values(job):
    """顺序扫描 G 代码，返回 [(line_no, text, 当前 Z, X, Y)]，用于核对 GRBL 实际 Z。"""
    cur_x = cur_y = cur_z = 0.0
    out = []
    for i, g in enumerate(job.gcode):
        t = g.text
        if not t.startswith("G0") and not t.startswith("G1"):
            continue
        mx = re.search(r"X(-?\d+(?:\.\d+)?)", t)
        my = re.search(r"Y(-?\d+(?:\.\d+)?)", t)
        mz = re.search(r"Z(-?\d+(?:\.\d+)?)", t)
        if mx:
            cur_x = float(mx.group(1))
        if my:
            cur_y = float(my.group(1))
        if mz:
            cur_z = float(mz.group(1))
        out.append((i + 1, t, cur_z, cur_x, cur_y))
    return out


print("=" * 70)
print("[1] 高度图补偿后的 Z 与 SafeZ 的关系（WriteZ=0, SafeZ=5, 纸面向右抬 8mm）")
cfg = base_cfg(UseHeightMap=True)
job = S.Job(cfg, FONTS, S.mixed_text())
job.hmap = tilted_map(z_at_right=8.0)
S.build(job, True)
zs = z_values(job)
hi = [(n, t) for (n, t, z, x, y) in zs if z > cfg.SafeZ]
print("  落笔 Z 高于抬笔 Z 的行数:", len(hi), "/", len(zs))
for n, t in hi[:6]:
    print("    line %d: %s" % (n, t))
lifts = [(n, t, z, x, y) for (n, t, z, x, y) in zs if t.startswith("G0 Z")]
buried = []
for (n, t, z, x, y) in lifts:
    px, py = S.to_paper((x, y), cfg)
    want = S.comp_z(job, px, py)
    if want > z:
        buried.append((n, x, y, z, want))
print("  抬笔行 G0 Z5 中，低于该处书写面(即笔已压入纸)的行数: %d/%d" % (len(buried), len(lifts)))
for r in buried[:5]:
    print("    line %d  X=%.1f Y=%.1f  G0 Z=%.1f 而该处需要 Z=%.2f（压深 %.2fmm）"
          % (r[0], r[1], r[2], r[3], r[4], r[4] - r[3]))
print("  校验器只看 m.SafeZ<=m.WriteZ → 5 > 0，无告警")

print()
print("=" * 70)
print("[2] 续写落在「连笔」笔画上：落笔指令缺失")
cfg = base_cfg(Connect=6.0, Eps=0.02)
job = S.Job(cfg, FONTS, S.short_text())
S.build(job, True)
line = S.find_resume_line(job, 1)
print("  FindResumeLine(CI=1) =", line, "总行数", len(job.gcode))
txt = S.to_text(job, line).split("\n")
for k, l in enumerate(txt[:10]):
    print("    %2d %s" % (k, l))
# 统计：续写文本里第一条落笔指令出现在第几行之后
first_z = None
for k, l in enumerate(txt):
    if l.startswith("G1 Z"):
        first_z = k
        break
print("  续写文本中第一条落笔 G1 Z 出现在第", first_z, "行（前缀 6 行）")
skipped = 0
for k, l in enumerate(txt):
    if l.startswith("G1 Z"):
        break
    if l.startswith("G1 X"):
        skipped += 1
print("  首条落笔之前空走的 XY 行数:", skipped)

print()
print("=" * 70)
print("[3] 行长（去掉空格后的字节数）")
mx = (0, "", None)
import glob
for p in glob.glob(r"D:\Writer\InkPen\dist\selftest\*.nc"):
    for i, l in enumerate(open(p, encoding="ascii", errors="replace").read().splitlines()):
        w = l.replace(" ", "")
        if len(w) > mx[0]:
            mx = (len(w), l, (p, i + 1))
print("  真实 .nc 最长:", mx[0], "字节  ", mx[1], " @", mx[2])
cfg = base_cfg(W=1000.0, H=1000.0, InvertX=True, InvertY=True, Origin="TopRight",
               UseHeightMap=True, XYFeed=20000.0, ZFeed=10000.0)
job = S.Job(cfg, FONTS, S.mixed_text())
job.hmap = {"cols": 3, "rows": 3, "minx": 0.0, "miny": 0.0, "maxx": 1000.0, "maxy": 1000.0,
            "z": [-9.999, 99.999, -99.999, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0]}
S.build(job, True)
mx2 = max((len(g.text.replace(" ", "")), g.text) for g in job.gcode)
print("  极端配置(纸 1000mm、负坐标、Z 三位小数、F20000) 最长:", mx2[0], "字节  ", mx2[1])

print()
print("=" * 70)
print("[4] Z 补偿的数值误差（含 0.005 阈值与 0.001 打印量化）")
cfg = base_cfg(UseHeightMap=True)
job = S.Job(cfg, FONTS, S.mixed_text())
job.hmap = tilted_map(z_at_right=1.5)
S.build(job, True)
maxerr = 0.0
worst = None
cur_z = None
for (n, t, z, x, y) in z_values(job):
    if t.startswith("G0 Z") or t.startswith("G1 Z"):
        cur_z = z
        continue
    if not t.startswith("G1 "):
        continue
    if "Z" in t:
        cur_z = z
    if cur_z is None:
        continue
    px, py = S.to_paper((x, y), cfg)
    ideal = S.comp_z(job, px, py)
    e = abs(cur_z - ideal)
    if e > maxerr:
        maxerr = e
        worst = (n, t, cur_z, ideal)
print("  最大 |实际Z - 目标Z| = %.5f mm   @line %d %r (实际 %.4f / 目标 %.4f)"
      % (maxerr, worst[0], worst[1], worst[2], worst[3]))

print()
print("=" * 70)
print("[5] 坐标变换 16 组合往返 + 机器坐标范围")
origins = ["BottomLeft", "TopLeft", "BottomRight", "TopRight"]
bad = 0
for o in origins:
    for ix in (False, True):
        for iy in (False, True):
            c = base_cfg(Origin=o, InvertX=ix, InvertY=iy)
            for p in [(0.0, 0.0), (210.0, 297.0), (37.5, 241.25), (209.999, 0.001)]:
                b = S.to_paper(S.to_machine(p, c), c)
                if abs(b[0] - p[0]) > 1e-9 or abs(b[1] - p[1]) > 1e-9:
                    bad += 1
print("  往返误差 > 1e-9 的组合数:", bad, "/ 64")
