"""离线 GCode 静态校验（不接机器）。用法: python validate_gcode.py <file.nc> [width_mm] [height_mm]

另支持: python validate_gcode.py --selftest  （纯内存自检，不读写文件）
"""
import io
import re
import sys
from collections import Counter

XY = re.compile(r"([XY])\s*(-?\d+(?:\.\d+)?)")
F = re.compile(r"F\s*(-?\d+(?:\.\d+)?)")
META = re.compile(r"\(CH:\d+;PG:-?\d+;LN:-?\d+;CI:-?\d+;SI:-?\d+\)")


def strip_gcode_comment(line):
    """Strip ';' line-comments that occur OUTSIDE parentheses.

    InkPen emits resume metadata such as (CH:20320;PG:0;LN:0;CI:0;SI:0)
    where the ';' are field separators *inside* parentheses. Those must
    survive intact so that META / CI matching still works. Only a ';' at
    parenthesis depth 0 starts a real G-code end-of-line comment.
    """
    depth = 0
    out = []
    for ch in line:
        if ch == "(":
            depth += 1
            out.append(ch)
        elif ch == ")":
            if depth > 0:
                depth -= 1
            out.append(ch)
        elif ch == ";" and depth == 0:
            break
        else:
            out.append(ch)
    return "".join(out).strip()


def main(argv):
    if len(argv) >= 2 and argv[1] == "--selftest":
        return selftest()
    if len(argv) < 2:
        print(__doc__)
        return 1
    path = argv[1]
    w = float(argv[2]) if len(argv) > 2 else 210.0
    h = float(argv[3]) if len(argv) > 3 else 297.0
    lines = io.open(path, encoding="ascii", errors="replace").read().splitlines()
    errors, warns = [], []
    z_count = feed_count = 0
    meta_count = 0
    char_indices = []
    for i, raw in enumerate(lines, 1):
        s = strip_gcode_comment(raw)
        if not s:
            continue
        # StreamController strips spaces before sending and hard-aborts above
        # GRBL 1.1's LINE_BUFFER_SIZE (80 bytes). Match that rule here so a
        # hand-edited .nc fails OFFLINE instead of at the machine.
        wire_bytes = len(s.replace(" ", "").encode("ascii", "replace"))
        if wire_bytes > 80:
            errors.append("行 %d 压缩后 %d 字节超过 GRBL 单行缓冲 80 字节，会被静默截断" % (i, wire_bytes))
        if META.search(s):
            meta_count += 1
            m = re.search(r"CI:(-?\d+)", s)
            if m:
                char_indices.append(int(m.group(1)))
        if "Z" in s and re.search(r"[G01]\d*\s", s):
            z_count += 1
        f = F.search(s)
        if f:
            feed_count += 1
            if float(f.group(1)) <= 0:
                errors.append("行 %d 进给率 <= 0" % i)
        for axis, v in XY.findall(s):
            val = float(v)
            lim = w if axis == "X" else h
            if abs(val) > lim + 1:
                errors.append("行 %d %s 越界: %.1f (幅面 %.1f)" % (i, axis, val, lim))
    if z_count == 0:
        errors.append("未发现 Z 抬落笔指令")
    if feed_count == 0:
        warns.append("未发现 F 进给率")
    if meta_count == 0:
        errors.append("未发现续写元数据注释 (CH/PG/LN/CI/SI)")
    dup = [k for k, v in Counter(char_indices).items() if v > 200]
    if dup:
        warns.append("字符序号 %s 的 GCode 行异常偏多，请检查路径简化" % dup[:5])

    print("文件:", path)
    print("行数:", len(lines), "| Z 指令:", z_count, "| F 指令:", feed_count,
          "| 续写注释:", meta_count, "| 字符数:", len(set(char_indices)))
    print("错误:", len(errors), "警告:", len(warns))
    for e in errors[:20]:
        print("  [ERR]", e)
    for x in warns[:20]:
        print("  [WARN]", x)
    print("RESULT:", "PASS" if not errors else "FAIL")
    return 0 if not errors else 2


def selftest():
    """Fast in-memory checks for comment stripping + META matching logic."""
    meta = "(CH:20320;PG:0;LN:0;CI:0;SI:0)"
    checks = [
        ("metadata survived intact (the original bug)",
         strip_gcode_comment(meta) == meta),
        ("META still matches", bool(META.search(strip_gcode_comment(meta)))),
        ("CI still extractable",
         re.search(r"CI:(-?\d+)", strip_gcode_comment(meta)) is not None),
        ("real trailing ';' comment stripped",
         strip_gcode_comment("G1 X10 Y20 ; pen down") == "G1 X10 Y20"),
        ("';' inside parens kept, real comment after parens stripped",
         strip_gcode_comment("G1 X1 Y2 (note;here) ; tail") == "G1 X1 Y2 (note;here)"),
        ("plain line unchanged (trimmed)",
         strip_gcode_comment("  G21 G90  ") == "G21 G90"),
        ("whole-line ';' comment -> empty",
         strip_gcode_comment("; pure comment") == ""),
    ]
    ok = True
    for name, passed in checks:
        print("  [%s] %s" % ("OK" if passed else "FAIL", name))
        ok = ok and passed
    print("SELFTEST:", "PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
