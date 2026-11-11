"""维度 B 复算工具：LayoutEngine + GCodeBuilder 的逐行移植（只读，不碰 src）。

用途：
  1) 用 dist/selftest/*.nc 做逐字节对照，确认移植可信；
  2) 在此基础上跑「高度图补偿 / 续写 / 行长」等边界场景，给出可复现数值。

本文件不修改任何生产文件，只放在 docs/_audit/tmp_b/。
"""
import io
import math
import os
import struct
import sys
import zipfile

GRID = 1024.0
NO_LINE_START = "\uFF0C\u3002\uFF01\uFF1F\uFF1B\uFF1A\u3001\uFF09\u3011\u300B\u300D\u300F\u3015\u3009\u2026\u00B7\uFF05\u2030.,!?;:)]}>"
NO_LINE_END = "\uFF08\u3010\u300A\u300C\u300E\u3014\u3008([{<"
ROOT = r"D:\Writer\InkPen"
GLYPH_DIR = os.path.join(ROOT, "data", "glyphs")
OUT_DIR = os.path.join(ROOT, "dist", "selftest")


# ---------- 字库 ----------
def unpack_glyph(data):
    if data.startswith(b"INKPGLY1"):
        data = data[8:]
    off = 0
    n_strokes, advance = struct.unpack_from("<Hf", data, off)
    off += 6
    strokes = []
    for _ in range(n_strokes):
        (n_pts,) = struct.unpack_from("<H", data, off)
        off += 2
        pts = []
        for _ in range(n_pts):
            x, y = struct.unpack_from("<ff", data, off)
            off += 8
            pts.append((x, y))
        strokes.append(pts)
    return strokes, advance


class Fonts(object):
    def __init__(self, prefer_local=False, latin="latin.zip"):
        order = []
        if prefer_local:
            order.append("local.zip")
        order += ["mmah.zip", "pinyin.zip", latin]
        if latin == "latin_script.zip":
            order.append("latin.zip")
        order.append("fallback.zip")
        if not prefer_local:
            order.append("local.zip")
        self.packs = []
        for name in order:
            path = os.path.join(GLYPH_DIR, name)
            if not os.path.exists(path):
                continue
            idx = {}
            with zipfile.ZipFile(path) as z:
                for n in z.namelist():
                    if n.isdigit():
                        idx[int(n)] = n
            self.packs.append((name, path, idx))
        self._cache = {}

    def get(self, ch):
        cp = ord(ch)
        for name, path, idx in self.packs:
            if cp not in idx:
                continue
            key = (name, cp)
            if key in self._cache:
                return self._cache[key]
            with zipfile.ZipFile(path) as z:
                strokes, adv = unpack_glyph(z.read(idx[cp]))
            strokes = [s for s in strokes if len(s) >= 2]
            g = (strokes, adv, cp)
            self._cache[key] = g
            return g
        return None


# ---------- 排版 ----------
class Glyph(object):
    __slots__ = ("ch", "strokes", "adv", "x", "y", "size", "adv_mm", "line", "ci", "pg")


class Page(object):
    def __init__(self):
        self.glyphs = []
        self.grid = []
        self.index = 0


class Cfg(object):
    W = 210.0
    H = 297.0
    ML = 15.0
    MR = 15.0
    MT = 15.0
    MB = 15.0
    FontSize = 10.0
    CharGap = 1.0
    LineGap = 6.0
    Indent = 0.0
    Justify = False
    Center = False
    Vertical = False
    Kind = "Free"
    DrawGrid = False
    Seed = 20261110
    JitterSize = 0.0
    JitterPos = 0.0
    JitterStroke = 0.0
    BaselineWave = 0.0
    TailStrength = 0.0
    Eps = 0.02
    SafeZ = 5.0
    WriteZ = 0.0
    ZFeed = 300.0
    XYFeed = 1500.0
    Dwell = 0.15
    Connect = 0.8
    StartGCode = "G21\nG90\nG94"
    EndGCode = "G0 X0 Y0"
    Origin = "BottomLeft"
    InvertX = False
    InvertY = False
    UseHeightMap = False


class _Rng(object):
    def __init__(self, seed):
        self.s = seed if seed != 0 else 0x9E3779B9

    def next(self):
        s = self.s
        s ^= (s << 13) & 0xFFFFFFFF
        s &= 0xFFFFFFFF
        s ^= s >> 17
        s ^= (s << 5) & 0xFFFFFFFF
        s &= 0xFFFFFFFF
        self.s = s
        return s

    def dbl(self):
        return (self.next() & 0xFFFFFF) / 16777216.0


def layout(cfg, fonts, text):
    usableW = cfg.W - cfg.ML - cfg.MR
    usableH = cfg.H - cfg.MT - cfg.MB
    lineH = cfg.FontSize + cfg.LineGap
    if lineH < 1:
        lineH = cfg.FontSize + 1
    pages = [Page()]
    pages[0].index = 0

    if cfg.Vertical:
        _layout_v(cfg, fonts, pages, text, usableW, lineH)
    else:
        _layout_h(cfg, fonts, pages, text, usableW, lineH)
    return pages


def _layout_h(cfg, fonts, pages, text, usableW, lineH):
    page = pages[0]
    x = cfg.ML + cfg.Indent
    y = cfg.H - cfg.MT - cfg.FontSize
    bottom = cfg.MB + cfg.FontSize
    line_index = 0
    char_index = 0
    line = []

    def flush():
        for p in line:
            p.line = line_index
            p.pg = page.index
            page.glyphs.append(p)

    for ch in text:
        if ch == "\n":
            flush()
            line.clear()
            line_index += 1
            x = cfg.ML
            y -= lineH
            if y < bottom:
                page = Page()
                page.index = len(pages)
                pages.append(page)
                y = cfg.H - cfg.MT - cfg.FontSize
                line_index = 0
            continue
        if ch == " ":
            x += cfg.FontSize * 0.5
            continue
        g = fonts.get(ch)
        if g is None:
            x += cfg.FontSize
            continue
        adv = max(g[1], 1) / GRID * cfg.FontSize + cfg.CharGap
        if x + adv > cfg.ML + usableW and len(line) > 0:
            last = line[-1]
            if ch in NO_LINE_START:
                pass                                    # 允许悬挂
            elif line and last.ch in NO_LINE_END:
                moved = line.pop()
                flush()
                line.clear()
                line.append(moved)
                moved.x = cfg.ML
                moved.y -= lineH
                line_index += 1
                moved.line = line_index
                x = cfg.ML + moved.adv_mm
                y -= lineH
            else:
                flush()
                line.clear()
                line_index += 1
                x = cfg.ML
                y -= lineH
            if y < bottom:
                page = Page()
                page.index = len(pages)
                pages.append(page)
                y = cfg.H - cfg.MT - cfg.FontSize
                line_index = 0
        p = Glyph()
        p.ch = ch
        p.strokes = g[0]
        p.adv = g[1]
        p.x = x
        p.y = y + math.sin(line_index * 0.9 + char_index * 0.13) * cfg.BaselineWave
        p.size = cfg.FontSize
        p.adv_mm = adv
        p.line = line_index
        p.ci = char_index
        char_index += 1
        line.append(p)
        x += adv
    flush()


def _layout_v(cfg, fonts, pages, text, usableW, lineH):
    page = pages[0]
    x = cfg.W - cfg.MR - cfg.FontSize
    y = cfg.H - cfg.MT - cfg.FontSize
    left = cfg.ML
    line_index = 0
    char_index = 0
    for ch in text:
        if ch == "\n":
            x -= lineH
            y = cfg.H - cfg.MT - cfg.FontSize
            line_index += 1
            if x < left:
                page = Page()
                page.index = len(pages)
                pages.append(page)
                x = cfg.W - cfg.MR - cfg.FontSize
                line_index = 0
            continue
        if ch == " ":
            y -= cfg.FontSize * 0.5
            continue
        g = fonts.get(ch)
        if g is None:
            y -= cfg.FontSize
            continue
        if y < cfg.MB:
            x -= lineH
            y = cfg.H - cfg.MT - cfg.FontSize
            line_index += 1
            if x < left:
                page = Page()
                page.index = len(pages)
                pages.append(page)
                x = cfg.W - cfg.MR - cfg.FontSize
                line_index = 0
        p = Glyph()
        p.ch = ch
        p.strokes = g[0]
        p.adv = g[1]
        p.x = x
        p.y = y
        p.size = cfg.FontSize
        p.adv_mm = cfg.FontSize + cfg.CharGap
        p.line = line_index
        p.ci = char_index
        char_index += 1
        p.pg = page.index
        page.glyphs.append(p)
        y -= p.adv_mm


# ---------- 坐标 ----------
def to_machine(p, cfg):
    x, y = p
    if cfg.Origin == "TopLeft":
        y = cfg.H - y
    elif cfg.Origin == "BottomRight":
        x = cfg.W - x
    elif cfg.Origin == "TopRight":
        x = cfg.W - x
        y = cfg.H - y
    if cfg.InvertX:
        x = -x
    if cfg.InvertY:
        y = -y
    return (x, y)


def to_paper(p, cfg):
    x, y = p
    if cfg.InvertX:
        x = -x
    if cfg.InvertY:
        y = -y
    if cfg.Origin == "TopLeft":
        y = cfg.H - y
    elif cfg.Origin == "BottomRight":
        x = cfg.W - x
    elif cfg.Origin == "TopRight":
        x = cfg.W - x
        y = cfg.H - y
    return (x, y)


# ---------- RDP ----------
def _dist(p, a, b):
    dx = b[0] - a[0]
    dy = b[1] - a[1]
    l2 = dx * dx + dy * dy
    if l2 < 1e-12:
        return math.sqrt((p[0] - a[0]) ** 2 + (p[1] - a[1]) ** 2)
    t = ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / l2
    t = 0.0 if t < 0 else (1.0 if t > 1 else t)
    qx = a[0] + t * dx
    qy = a[1] + t * dy
    return math.sqrt((p[0] - qx) ** 2 + (p[1] - qy) ** 2)


def _simp(pts, a, b, eps, keep):
    if b <= a + 1:
        return
    maxd = -1.0
    idx = -1
    for i in range(a + 1, b):
        d = _dist(pts[i], pts[a], pts[b])
        if d > maxd:
            maxd = d
            idx = i
    if maxd > eps and idx > 0:
        keep[idx] = True
        _simp(pts, a, idx, eps, keep)
        _simp(pts, idx, b, eps, keep)


def rdp(pts, eps):
    if pts is None:
        return []
    if len(pts) < 3 or eps <= 0:
        return list(pts)
    keep = [False] * len(pts)
    keep[0] = keep[-1] = True
    _simp(pts, 0, len(pts) - 1, eps, keep)
    return [p for i, p in enumerate(pts) if keep[i]]


def enhance_tail(pts, strength):
    if pts is None or len(pts) < 2 or strength <= 0:
        return pts
    r = list(pts)
    a, b = pts[0], pts[1]
    ln = math.hypot(b[0] - a[0], b[1] - a[1])
    if ln > 1e-6:
        k = min(strength, 0.2) * min(ln, 2.0) / ln
        r.insert(0, (a[0] + (a[0] - b[0]) * k, a[1] + (a[1] - b[1]) * k))
    c, d = pts[-2], pts[-1]
    ln2 = math.hypot(d[0] - c[0], d[1] - c[1])
    if ln2 > 1e-6:
        k = min(strength, 0.2) * min(ln2, 2.0) / ln2
        r.append((d[0] + (d[0] - c[0]) * k, d[1] + (d[1] - c[1]) * k))
    return r


# ---------- 格式化（.NET "0.###"，四舍五入到 3 位） ----------
def F(v):
    # .NET "0.###"：四舍五入（half away from zero）到 3 位小数
    if v == 0:
        v = 0.0
    a = abs(v)
    q = math.floor(a * 1000.0 + 0.5) / 1000.0
    s = ("%.3f" % q)
    if "." in s:
        s = s.rstrip("0").rstrip(".")
    if s == "":
        s = "0"
    return ("-" + s) if (v < 0 and q != 0) else s


class CodeLine(object):
    __slots__ = ("text", "pg", "ln", "ci", "si", "cp", "down")

    def __init__(self, text, pg=-1, ln=-1, ci=-1, si=-1, cp=0, down=False):
        self.text = text
        self.pg = pg
        self.ln = ln
        self.ci = ci
        self.si = si
        self.cp = cp
        self.down = down


def build_grid(cfg, page):
    page.grid = []
    if cfg.Kind == "Free":
        return
    mL = cfg.ML
    mR = cfg.W - cfg.MR
    lineH = cfg.FontSize + cfg.LineGap
    top = cfg.H - cfg.MT

    if cfg.Vertical:
        x = cfg.W - cfg.MR - cfg.FontSize
        while x > cfg.ML:
            if cfg.Kind in ("Grid", "Tianzi"):
                page.grid.append((x, cfg.MB, x, top))
            x -= lineH
        return

    baselines = []
    for g in page.glyphs:
        b = g.y
        found = False
        for v in baselines:
            if abs(v - b) < lineH * 0.4:
                found = True
                break
        if not found:
            baselines.append(b)
    baselines.sort(reverse=True)
    for base_y in baselines:
        if cfg.Kind == "Ruled":
            page.grid.append((mL, base_y, mR, base_y))
        elif cfg.Kind == "Pinyin4":
            q = cfg.FontSize / 3.0
            for k in range(4):
                page.grid.append((mL, base_y + k * q, mR, base_y + k * q))
        else:
            cell = cfg.FontSize + cfg.CharGap
            x = mL
            while x + cell <= mR + 0.01:
                page.grid.append((x, base_y, x + cell, base_y))
                page.grid.append((x, base_y + cfg.FontSize, x + cell, base_y + cfg.FontSize))
                page.grid.append((x, base_y, x, base_y + cfg.FontSize))
                page.grid.append((x + cell, base_y, x + cell, base_y + cfg.FontSize))
                if cfg.Kind == "Tianzi":
                    for k in range(5):
                        a = base_y + k * cfg.FontSize / 5.0
                        page.grid.append((x + cell / 2, a, x + cell / 2, a + cfg.FontSize / 10.0))
                        bx = x + k * cell / 5.0
                        page.grid.append((bx, base_y + cfg.FontSize / 2, bx + cell / 10.0, base_y + cfg.FontSize / 2))
                x += cell


class Job(object):
    def __init__(self, cfg, fonts, text):
        self.cfg = cfg
        self.pages = layout(cfg, fonts, text)
        for p in self.pages:
            build_grid(cfg, p)
        self.gcode = []
        self.paths = []
        self.hmap = None          # dict(minx,miny,maxx,maxy,cols,rows,z[])


def comp_z(job, px, py):
    cfg = job.cfg
    if not cfg.UseHeightMap:
        return cfg.WriteZ
    hm = job.hmap
    if hm is None:
        return cfg.WriteZ
    return cfg.WriteZ + hm_at(hm, px, py)


def hm_at(hm, x, y):
    cols, rows = hm["cols"], hm["rows"]
    minx, miny, maxx, maxy = hm["minx"], hm["miny"], hm["maxx"], hm["maxy"]
    if not (maxx > minx and maxy > miny):
        return 0.0
    u = (x - minx) / (maxx - minx) * (cols - 1)
    v = (y - miny) / (maxy - miny) * (rows - 1)
    if math.isnan(u) or math.isnan(v):
        return 0.0
    u = 0.0 if u < 0 else (cols - 1 if u > cols - 1 else u)
    v = 0.0 if v < 0 else (rows - 1 if v > rows - 1 else v)
    c0 = int(math.floor(u))
    r0 = int(math.floor(v))
    if c0 > cols - 2:
        c0 = cols - 2
    if r0 > rows - 2:
        r0 = rows - 2
    if c0 < 0:
        c0 = 0
    if r0 < 0:
        r0 = 0
    fx = u - c0
    fy = v - r0

    def get(c, r):
        return hm["z"][r * cols + c]

    z00, z10 = get(c0, r0), get(c0 + 1, r0)
    z01, z11 = get(c0, r0 + 1), get(c0 + 1, r0 + 1)
    a = z00 + (z10 - z00) * fx
    b = z01 + (z11 - z01) * fx
    return a + (b - a) * fy


def emit(job, text, pg=-1, ln=-1, ci=-1, si=-1, cp=0, down=False):
    job.gcode.append(CodeLine(text, pg, ln, ci, si, cp, down))


def add_lines(job, block):
    if not block:
        return
    for l in block.replace("\r\n", "\n").split("\n"):
        s = l.strip()
        if s:
            emit(job, s)


def build(job, include_grid=True, verbose_log=False):
    cfg = job.cfg
    job.gcode = []
    job.paths = []
    add_lines(job, cfg.StartGCode)
    emit(job, "(INKPEN JOB START)")
    emit(job, "G21 G90 G94")
    emit(job, "F" + F(cfg.XYFeed))

    pen_down = False
    last_x = last_y = 0.0
    last_z = cfg.WriteZ
    for page in job.pages:
        if include_grid and cfg.DrawGrid:
            for (x1, y1, x2, y2) in page.grid:
                st = {"pen": pen_down, "lx": last_x, "ly": last_y, "lz": last_z}
                _emit_path(job, [(x1, y1), (x2, y2)], st, page.index, -1, -1, -1, 0)
                pen_down, last_x, last_y, last_z = st["pen"], st["lx"], st["ly"], st["lz"]
        for pg in page.glyphs:
            for si, stroke in enumerate(pg.strokes):
                raw = []
                for pi, g in enumerate(stroke):
                    raw.append((pg.x + g[0] / 1024.0 * pg.size,
                                pg.y + g[1] / 1024.0 * pg.size))
                pts = rdp(enhance_tail(raw, cfg.TailStrength), cfg.Eps)
                if len(pts) < 2:
                    continue
                first = to_machine(pts[0], cfg)
                gap = math.hypot(first[0] - last_x, first[1] - last_y)
                if pen_down and gap > cfg.Connect:
                    emit(job, "G0 Z" + F(cfg.SafeZ))
                    pen_down = False
                st = {"pen": pen_down, "lx": last_x, "ly": last_y, "lz": last_z}
                _emit_path(job, pts, st, page.index, pg.line, pg.ci, si, ord(pg.ch))
                pen_down, last_x, last_y, last_z = st["pen"], st["lx"], st["ly"], st["lz"]
    if pen_down:
        emit(job, "G0 Z" + F(cfg.SafeZ))
    emit(job, "(INKPEN JOB END)")
    add_lines(job, cfg.EndGCode)


ZCOMP_EPS = 0.005


def _emit_path(job, paper_pts, st, page_idx, line_idx, char_idx, stroke_idx, cp):
    cfg = job.cfg
    machine = [to_machine(p, cfg) for p in paper_pts]
    start = machine[0]
    comp = cfg.UseHeightMap and job.hmap is not None
    z0 = comp_z(job, paper_pts[0][0], paper_pts[0][1])

    st["first_line"] = len(job.gcode)
    job.paths.append({"first": len(job.gcode), "n": len(paper_pts),
                      "ci": char_idx, "si": stroke_idx, "cp": cp})
    meta = "(CH:%d;PG:%d;LN:%d;CI:%d;SI:%d)" % (cp, page_idx, line_idx, char_idx, stroke_idx)
    emit(job, meta, page_idx, line_idx, char_idx, stroke_idx, cp, False)
    if not st["pen"]:
        emit(job, "G0 X" + F(start[0]) + " Y" + F(start[1]), page_idx, line_idx, char_idx, stroke_idx, cp, False)
        emit(job, "G1 Z" + F(z0) + " F" + F(cfg.ZFeed), page_idx, line_idx, char_idx, stroke_idx, cp, False)
        if cfg.Dwell > 0:
            emit(job, "G4 P" + F(cfg.Dwell))
        st["pen"] = True
        st["lz"] = z0
    elif comp and abs(z0 - st["lz"]) >= ZCOMP_EPS:
        emit(job, "G1 Z" + F(z0) + " F" + F(cfg.ZFeed), page_idx, line_idx, char_idx, stroke_idx, cp, False)
        st["lz"] = z0

    for i in range(1, len(machine)):
        p = machine[i]
        zseg = ""
        if comp:
            pz = comp_z(job, paper_pts[i][0], paper_pts[i][1])
            if abs(pz - st["lz"]) >= ZCOMP_EPS:
                zseg = " Z" + F(pz)
                st["lz"] = pz
        if i == 1:
            emit(job, "G1 X" + F(p[0]) + " Y" + F(p[1]) + zseg + " F" + F(cfg.XYFeed),
                 page_idx, line_idx, char_idx, stroke_idx, cp, True)
        else:
            emit(job, "G1 X" + F(p[0]) + " Y" + F(p[1]) + zseg,
                 page_idx, line_idx, char_idx, stroke_idx, cp, True)
    st["lx"] = machine[-1][0]
    st["ly"] = machine[-1][1]


def to_text(job, from_line):
    sb = []
    if from_line > 0:
        sb.append("(RESUME FROM LINE %d)" % (from_line + 1))
        sb.append("G0 Z" + F(job.cfg.SafeZ))
        sb.append("G21")
        sb.append("G90")
        sb.append("G94")
        sb.append("F" + F(job.cfg.XYFeed))
    for g in job.gcode[from_line:]:
        sb.append(g.text)
    return "\n".join(sb) + "\n"


def find_resume_line(job, char_index):
    if char_index <= 0:
        return 0
    for i, g in enumerate(job.gcode):
        if g.ci >= char_index and g.si >= 0:
            return i
    return len(job.gcode)


# ---------- 文本 ----------
def chars(*cps):
    return "".join(chr(c) for c in cps)


def mixed_text():
    return (chars(0x6C49, 0x5B57, 0x6570, 0x5B57) + "123 Latin "
            + "m" + chars(0x0101) + " m" + chars(0x00E1) + " m" + chars(0x01CE) + " m" + chars(0x00E0) + " "
            + "n" + chars(0x01D0) + " h" + chars(0x01CE) + "o "
            + chars(0x01D6) + " " + chars(0x01D8) + " " + chars(0x01DA) + " " + chars(0x01DC) + " "
            + chars(0xFF0C, 0x3002, 0xFF01, 0xFF1F, 0xFF1B, 0xFF1A, 0x300A, 0x300B, 0x3001))


def short_text():
    return "InkPen " + chars(0x6C49, 0x5B57) + "123 abc " + chars(0xFF0C, 0x3002, 0xFF01)


def compare(name, cfg, fonts, text):
    job = Job(cfg, fonts, text)
    build(job, True)
    got = to_text(job, 0).replace("\n", "\n")  # 原文
    path = os.path.join(OUT_DIR, name + ".nc")
    with io.open(path, encoding="ascii", errors="replace") as f:
        want = f.read()
    ok = got == want
    if not ok:
        a = got.split("\n")
        b = want.split("\n")
        print("  MISMATCH %s: lines %d vs %d" % (name, len(a), len(b)))
        for i in range(min(len(a), len(b))):
            if a[i] != b[i]:
                print("    first diff at %d: got %r want %r" % (i + 1, a[i], b[i]))
                break
    return ok


def multi_text():
    phrase = chars(0x6C49, 0x5B57, 0x6570, 0x5B57) + "123abc" + chars(0xFF0C)
    return "\n".join([phrase] * 60)


if __name__ == "__main__":
    fonts = Fonts(False, "latin.zip")
    cfgs = [
        ("b_free", dict(Kind="Free"), short_text()),
        ("a_mixed", dict(Kind="Free"), mixed_text()),
        ("c_vertical", dict(Kind="Ruled", Vertical=True), mixed_text()),
        ("d_multipage", dict(Kind="Ruled"), multi_text()),
        ("e_drawgrid", dict(Kind="Grid", DrawGrid=True), short_text()),
        ("b_tianzi", dict(Kind="Tianzi"), short_text()),
        ("b_grid", dict(Kind="Grid"), short_text()),
        ("b_pinyin4", dict(Kind="Pinyin4"), short_text()),
    ]
    allok = True
    for name, kw, text in cfgs:
        cfg = Cfg()
        for k, v in kw.items():
            setattr(cfg, k, v)
        ok = compare(name, cfg, fonts, text)
        print(("  OK   " if ok else "  FAIL ") + name)
        allok = allok and ok
    print("PORT MATCH:", allok)
