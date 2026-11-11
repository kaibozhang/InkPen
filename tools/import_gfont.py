"""
导入本机奎享雕刻 .gfont 手写单线字库 -> data/glyphs/local.zip（仅本机使用，不随软件分发）。

已确证的 .gfont 格式：
  文件 = 43,340 B 不透明前缀 + 标准 ZIP(Deflate)
  ZIP 条目名 = 十进制 Unicode 码点
  条目数据 = uint16 码点 + uint32 有效 float 数（均大端）
             + N 个 float32 (x,y) 交替（大端），尾部补 0 填充
  无 G-code、无笔顺字段、无抬笔标记；笔画边界仅由点序隐含

降级策略（用户批准）：启发式断笔 + C# 笔画编辑器人工修正。
断笔判据：相邻点距离超阈值 且 前一笔画方向与跳变方向夹角大（余弦 < COS_LIMIT）。
"""
import io
import json
import math
import os
import struct
import sys
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from glyph_format import GRID, write_font_zip  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "data" / "glyphs"

DEFAULT_GFONT = Path(r"C:\Program Files (x86)\kdraw(x64)\gcodeFonts\blood8真迹_kvenjoy_blood8.gfont")

# 启发式断笔参数（可在 docs 里调）
DIST_FACTOR = 2.5       # 距离 > 2.5 * 中位步长
DIAG_FACTOR = 0.12      # 且 > 0.12 * 字形对角线
COS_LIMIT = 0.35        # 且前段方向与跳变方向余弦 < 0.35


def find_gfont(explicit=None):
    if explicit and Path(explicit).is_file():
        return Path(explicit)
    if DEFAULT_GFONT.is_file():
        return DEFAULT_GFONT
    roots = [r"C:\Program Files (x86)\kdraw(x64)", r"C:\Program Files\kdraw(x64)"]
    for r in roots:
        if not os.path.isdir(r):
            continue
        for dp, _, fn in os.walk(r):
            for f in fn:
                if f.lower().endswith(".gfont"):
                    return Path(dp) / f
    return None


def open_zip(path):
    b = path.read_bytes()
    i = b.find(b"PK\x03\x04")
    if i < 0:
        raise RuntimeError("ZIP header not found in .gfont")
    return zipfile.ZipFile(io.BytesIO(b[i:])), i


def parse_entry(data):
    if len(data) < 6:
        return None
    cp, cnt = struct.unpack_from(">HI", data, 0)
    need = 6 + cnt * 4
    if need > len(data):
        cnt = (len(data) - 6) // 4
    vals = struct.unpack_from(">" + "f" * cnt, data, 6)
    pts = [(vals[j], vals[j + 1]) for j in range(0, cnt - 1, 2)]
    # 去掉尾部 (0,0) 填充点
    while pts and abs(pts[-1][0]) < 1e-6 and abs(pts[-1][1]) < 1e-6:
        pts.pop()
    return cp, pts


def dist(a, b):
    return math.hypot(b[0] - a[0], b[1] - a[1])


def unit(v):
    n = math.hypot(v[0], v[1])
    return (v[0] / n, v[1] / n) if n > 1e-9 else (0.0, 0.0)


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1]


def split_strokes(pts):
    """启发式断笔：距离超阈值 + 方向突变。"""
    if len(pts) < 3:
        return [pts] if len(pts) >= 2 else []
    steps = [dist(pts[j - 1], pts[j]) for j in range(1, len(pts))]
    ordered = sorted(steps)
    median = ordered[len(ordered) // 2] if ordered else 0.0
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    diag = math.hypot(max(xs) - min(xs), max(ys) - min(ys))
    d_thresh = max(DIST_FACTOR * median, DIAG_FACTOR * diag)

    cuts = []
    for j in range(2, len(pts) - 1):
        d = dist(pts[j - 1], pts[j])
        if d <= d_thresh:
            continue
        v_prev = unit((pts[j - 1][0] - pts[j - 2][0], pts[j - 1][1] - pts[j - 2][1]))
        v_jump = unit((pts[j][0] - pts[j - 1][0], pts[j][1] - pts[j - 1][1]))
        if dot(v_prev, v_jump) < COS_LIMIT:
            cuts.append(j)
    strokes, start = [], 0
    for j in cuts:
        if j - start >= 2:
            strokes.append(pts[start:j])
        start = j
    if len(pts) - start >= 2:
        strokes.append(pts[start:])
    return strokes


def global_range(z):
    lo_x = lo_y = float("inf")
    hi_x = hi_y = float("-inf")
    n = 0
    for name in z.namelist():
        if not name.isdigit():
            continue
        r = parse_entry(z.read(name))
        if not r or not r[1]:
            continue
        for x, y in r[1]:
            lo_x, hi_x = min(lo_x, x), max(hi_x, x)
            lo_y, hi_y = min(lo_y, y), max(hi_y, y)
        n += 1
    return lo_x, lo_y, hi_x, hi_y, n


def main(argv=None):
    argv = argv or sys.argv[1:]
    path = find_gfont(argv[0] if argv else None)
    if not path:
        print("ERROR: .gfont not found; pass explicit path")
        return 2
    print("gfont:", path, path.stat().st_size, "bytes")
    z, off = open_zip(path)
    print("ZIP starts at byte", off, "entries", len(z.namelist()))
    lo_x, lo_y, hi_x, hi_y, n = global_range(z)
    print("raw coord range x[%.2f,%.2f] y[%.2f,%.2f] over %d glyphs" % (lo_x, hi_x, lo_y, hi_y, n))
    span = max(hi_x - lo_x, hi_y - lo_y, 1e-6)
    scale = GRID / span
    off_x = (GRID - (hi_x - lo_x) * scale) / 2.0 - lo_x * scale
    off_y = (GRID - (hi_y - lo_y) * scale) / 2.0 - lo_y * scale

    glyphs, suspicious, stroke_counts = {}, [], []
    for name in z.namelist():
        if not name.isdigit():
            continue
        r = parse_entry(z.read(name))
        if not r or not r[1]:
            continue
        cp, pts = r
        strokes = split_strokes(pts)
        if not strokes:
            continue
        # gfont 原始坐标 Y 向下（奎享按屏幕/GCode 约定导出），而 InkPen Glyph v1
        # 规定 1024 网格原点在左下、Y 向上。这里必须镜像，否则整包字上下颠倒。
        conv = [[(x * scale + off_x, GRID - (y * scale + off_y)) for x, y in s] for s in strokes]
        glyphs[cp] = (conv, GRID)
        stroke_counts.append(len(strokes))
        if len(strokes) > 20 or len(strokes) == 0:
            suspicious.append((cp, len(strokes)))

    write_font_zip(OUT / "local.zip", glyphs,
                   "Local .gfont import (private, NOT distributed). "
                   "Heuristic stroke splitting; stroke order NOT verified; fix in StrokeEditor.")
    avg = sum(stroke_counts) / max(1, len(stroke_counts))
    report = {
        "source": str(path),
        "prefix_bytes": off,
        "glyphs": len(glyphs),
        "avg_strokes": round(avg, 2),
        "transform": {"scale": scale, "offX": off_x, "offY": off_y, "flipY": True},
        "params": {"DIST_FACTOR": DIST_FACTOR, "DIAG_FACTOR": DIAG_FACTOR, "COS_LIMIT": COS_LIMIT},
        "suspicious": suspicious[:200],
        "note": "笔画边界为启发式推断，笔顺未校验；请在 C# 笔画编辑器中人工修正高频字。"
    }
    io.open(OUT / "local_report.json", "w", encoding="utf-8").write(
        json.dumps(report, ensure_ascii=False, indent=2))
    print("imported:", len(glyphs), "glyphs, avg strokes", round(avg, 2),
          "suspicious", len(suspicious))
    print("output:", OUT / "local.zip")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
