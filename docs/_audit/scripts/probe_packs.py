"""只读探测：字库包的条目/advance/边界异常。不写任何项目文件。"""
import struct
import sys
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(r"D:/Writer/InkPen/tools")))
from glyph_format import MAGIC, GRID

G = Path(r"D:/Writer/InkPen/data/glyphs")
FILES = ["local.zip", "mmah.zip", "fallback.zip", "latin.zip", "latin_script.zip", "pinyin.zip"]

def decode(data):
    magic = data.startswith(MAGIC)
    if magic:
        data = data[len(MAGIC):]
    off = 0
    n_strokes, adv = struct.unpack_from("<Hf", data, off)
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
    return strokes, adv, magic, off, len(data)

for f in FILES:
    p = G / f
    if not p.is_file():
        print(f, "MISSING")
        continue
    z = zipfile.ZipFile(p)
    names = z.namelist()
    nonnum = [n for n in names if not n.isdigit()]
    cps = [int(n) for n in names if n.isdigit()]
    nonbmp = [c for c in cps if c > 0xFFFF]
    dups = len(cps) - len(set(cps))
    advs = {}
    magic_n = 0
    trail = 0
    zero_adv = 0
    empty = 0
    badbbox = 0
    onept = 0
    cjk_adv = {}
    for n in names:
        if not n.isdigit():
            continue
        st, adv, magic, used, total = decode(z.read(n))
        if magic:
            magic_n += 1
        if used != total:
            trail += 1
        advs[round(adv, 3)] = advs.get(round(adv, 3), 0) + 1
        if adv <= 0:
            zero_adv += 1
        if not st or all(len(s) < 2 for s in st):
            empty += 1
        if any(len(s) == 1 for s in st):
            onept += 1
        xs = [q[0] for s in st for q in s]
        ys = [q[1] for s in st for q in s]
        if xs and (min(xs) < -1 or min(ys) < -1 or max(xs) > GRID + 1 or max(ys) > GRID + 1):
            badbbox += 1
    top = sorted(advs.items(), key=lambda kv: -kv[1])[:8]
    print("== %s entries=%d nonnum=%s nonbmp=%d dups=%d magic=%d trailbytes=%d zero_adv=%d empty=%d onept=%d badbbox=%d"
          % (f, len(cps), nonnum[:3], len(nonbmp), dups, magic_n, trail, zero_adv, empty, onept, badbbox))
    print("   advance top:", top)
    z.close()
