"""
InkPen 开放字库格式（v1）。

每字库一个 ZIP；条目名 = 十进制 Unicode 码点；条目内容为紧凑二进制笔画数据。
坐标统一为 1024x1024、Y 向上、原点左下；只存墨迹；显式笔画边界与抬笔标记。

条目二进制（小端）：
    uint16  strokeCount
    float32 advance         （字身推进宽度，1024 网格单位；汉字通常 1024）
    每笔:
      uint16  pointCount
      float32 x, float32 y   （重复 pointCount 次）

设计取舍：C# 端用 System.IO.Compression.ZipFile + BinaryReader 即可零依赖读取；
        人可读的 JSON 等价物由 validate_glyphs.py 按需导出到 docs/。
"""
import struct
import zipfile
from pathlib import Path

GRID = 1024.0
MAGIC = b"INKPGLY1"


def pack_glyph(strokes, advance=GRID):
    """strokes: list[list[(x, y)]] -> bytes"""
    out = bytearray(struct.pack("<Hf", len(strokes), float(advance)))
    for stroke in strokes:
        pts = [(float(x), float(y)) for x, y in stroke] if len(stroke) >= 2 else []
        out.extend(struct.pack("<H", len(pts)))
        for x, y in pts:
            out.extend(struct.pack("<ff", x, y))
    return bytes(out)


def unpack_glyph(data):
    """bytes -> (strokes, advance)"""
    if data.startswith(MAGIC):
        data = data[len(MAGIC):]
    off = 0
    (n_strokes, advance) = struct.unpack_from("<Hf", data, off)
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


def write_font_zip(path, glyphs, source_note=""):
    """glyphs: dict[int, (strokes, advance)] 或 dict[int, strokes] -> zip file"""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for cp in sorted(glyphs):
            item = glyphs[cp]
            if isinstance(item, tuple):
                strokes, advance = item
            else:
                strokes, advance = item, GRID
            if not strokes:
                continue
            z.writestr(str(cp), pack_glyph(strokes, advance))
        if source_note:
            z.writestr("_SOURCE.txt", source_note)
    return path


def read_font_zip(path):
    out = {}
    with zipfile.ZipFile(path, "r") as z:
        for name in z.namelist():
            if not name.isdigit():
                continue
            out[int(name)] = unpack_glyph(z.read(name))
    return out


def normalize(strokes, box=GRID, margin=0.0, target_aspect=1.0):
    """把任意坐标的笔画线性归一化到 [margin, box-margin] 的方形网格。
    保持整体纵横比（按较大边等比缩放并居中），不单独拉伸 X/Y。
    """
    pts = [p for s in strokes for p in s]
    if not pts:
        return []
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    min_x, max_x, min_y, max_y = min(xs), max(xs), min(ys), max(ys)
    w, h = max_x - min_x, max_y - min_y
    # 汉字按字身方形铺满；拉丁等窄字形按高度的给定比例处理
    span = max(w, h, 1e-6)
    scale = (box - 2 * margin) / span
    off_x = (box - w * scale) / 2.0
    off_y = (box - h * scale) / 2.0
    out = []
    for s in strokes:
        out.append([((x - min_x) * scale + off_x, (y - min_y) * scale + off_y) for x, y in s])
    return out


def strokes_bbox(strokes):
    pts = [p for s in strokes for p in s]
    if not pts:
        return (0, 0, 0, 0)
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    return (min(xs), min(ys), max(xs), max(ys))
