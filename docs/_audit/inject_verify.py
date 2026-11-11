# -*- coding: utf-8 -*-
"""注入验证：把本轮每个修复改回「坏实现」，确认对应的回归用例会变红。

通过的标准不是「测试还是绿的」，而是**指定那条用例必须红**。
每个注入独立跑一次 test.bat，跑完立刻还原并校验文件指纹。
"""
import hashlib
import io
import os
import re
import shutil
import subprocess
import sys

ROOT = r"D:\Writer\InkPen"
BAK = os.path.join(ROOT, "docs", "_audit", "_inj_backup")
TESTBAT = os.path.join(ROOT, "build", "test.bat")


def md5(p):
    h = hashlib.md5()
    with open(p, "rb") as f:
        h.update(f.read())
    return h.hexdigest()


def rd(p):
    return io.open(p, encoding="utf-8").read()


def wr(p, s):
    io.open(p, "w", encoding="utf-8", newline="").write(s)


# (名字, 相对路径, 好实现片段, 坏实现片段, 期望变红的用例名)
INJECTIONS = [
    ("B1 抬笔不补偿",
     r"src\App\Core\GCode\GCodeBuilder.cs",
     "double liftZ = SafeLiftZ(job);",
     "double liftZ = m.SafeZ;",
     "PenUp_TracksTiltedPaper_NotRawSafeZ"),

    ("B5 续写前缀顺序颠倒",
     r"src\App\Core\GCode\GCodeBuilder.cs",
     'sb.Append("G21\\nG90\\nG94\\n");\n                sb.Append("G0 Z" + F(SafeLiftZ(job)) + "\\n");',
     'sb.Append("G0 Z" + F(SafeLiftZ(job)) + "\\n");\n                sb.Append("G21\\nG90\\nG94\\n");',
     "ResumePrefix_DeclaresUnitsBeforeAnyZMove"),

    ("A1 悬挂标点无上限",
     r"src\App\Core\Layout\LayoutEngine.cs",
     "bool hangOk = NO_LINE_START.IndexOf(ch) >= 0\n                               && (x + adv) - (t.MarginLeft + usableW) <= t.FontSizeMm * 1.05;",
     "bool hangOk = NO_LINE_START.IndexOf(ch) >= 0;",
     "HangingPunctuation_CannotWalkOffThePaper"),

    ("A6 空格越界不换行",
     r"src\App\Core\Layout\LayoutEngine.cs",
     "double sp = t.FontSizeMm * 0.5;",
     'x += t.FontSizeMm * 0.5; continue;\n                    double sp = t.FontSizeMm * 0.5;',
     "SpacesAtLineStart_CannotPushTextOffThePaper"),

    ("A8 缺字错误累积",
     r"src\App\Core\Glyph\GlyphRepository.cs",
     "            LoadErrors.Clear();\n",
     "",
     "Load_DoesNotAccumulateErrorsAcrossReloads"),

    ("C2 丢弃 WCO 换算",
     r"src\App\Core\Grbl\GrblCore.cs",
     "            if (!s.HasWPos && s.HasMPos && s.SawWco)",
     "            if (false && !s.HasWPos && s.HasMPos && s.SawWco)",
     "SetStatus_MposPlusWco_IsConvertedToWorkPosition"),

    ("B6 续写不补落笔",
     r"src\App\Core\GCode\GCodeBuilder.cs",
     "                if (TryFindResumePenDownZ(job, fromLine, out resumeZ))\n                    sb.Append(\"G1 Z\" + F(resumeZ) + \" F\" + F(job.Machine.ZFeed) + \"\\n\");",
     "                resumeZ = 0; if (false) {}",
     "ResumeIntoConnectedStroke_ReAppliesPenDownZ"),

    ("A3 对齐补量忽略缩进",
     r"src\App\Core\Layout\LayoutEngine.cs",
     "double extra = (t.MarginLeft + usableW) - (lineStartX + used);",
     "double extra = usableW - used;",
     "JustifyWithFirstLineIndent_DoesNotOvershootRightMargin"),

    ("A4 页底多扣一个字号",
     r"src\App\Core\Layout\LayoutEngine.cs",
     "double bottom = t.MarginBottom;",
     "double bottom = t.MarginBottom + t.FontSizeMm;",
     "PageBottom_DoesNotWasteAnExtraFontHeight"),

    ("A5 格线忽略缩进",
     r"src\App\Core\Layout\LayoutEngine.cs",
     "if (indent > 0 && (t.Kind == TemplateKind.Grid || t.Kind == TemplateKind.Tianzi))",
     "if (false && indent > 0 && (t.Kind == TemplateKind.Grid || t.Kind == TemplateKind.Tianzi))",
     "GridIndent_SnapsToCellBoundary"),

    # 注意：注入点必须落在「真正让它变对」的那一句上。
    # A7 首版注入的是 Get(cp) → Get(ch)，结果用例仍绿 —— 因为缺字计数与宽度推进
    # 都由排版循环里的代理对合并决定，Get 重载只影响「字库将来收录了也取不到」。
    ("A7 代理对不合并",
     r"src\App\Core\Layout\LayoutEngine.cs",
     "if (char.IsHighSurrogate(ch) && i + 1 < text.Length",
     "if (false && char.IsHighSurrogate(ch) && i + 1 < text.Length",
     "SurrogatePair_IsOneCharNotTwo"),

    # B4 的坏实现是「整段原逻辑」：放行 2×N 网格，且边界按夹取后的邻域取平均。
    # 这两件事必须一起退回去，否则 3×2 网格根本没有内部点、平滑跑不到，
    # 用例会假绿。
    ("B4 小网格削峰",
     r"src\App\Core\Machine\HeightMap.cs",
     "            if (Z == null || Cols < 3 || Rows < 3) return;",
     "            if (Z == null || Cols < 2 || Rows < 2) return;\n"
     "            {\n"
     "                double[] _s = (double[])Z.Clone();\n"
     "                for (int _r = 0; _r < Rows; _r++)\n"
     "                    for (int _c = 0; _c < Cols; _c++)\n"
     "                    {\n"
     "                        double _sum = 0;\n"
     "                        for (int _dr = -1; _dr <= 1; _dr++)\n"
     "                            for (int _dc = -1; _dc <= 1; _dc++)\n"
     "                                _sum += _s[Math.Min(Rows - 1, Math.Max(0, _r + _dr)) * Cols\n"
     "                                        + Math.Min(Cols - 1, Math.Max(0, _c + _dc))];\n"
     "                        Z[_r * Cols + _c] = _sum / 9.0;\n"
     "                    }\n"
     "            }\n"
     "            if (Cols > 0) return;",
     "Smooth_KeepsSingleAxisTiltOnSmallGrid"),

    ("C8 迟到 PRB 错位记账",
     r"src\App\Core\Machine\ProbeRunner.cs",
     "int owner = _prbWait.Count > 0 ? _prbWait.Dequeue() : _index;\n                if (owner != _index)",
     "int owner = _index;\n                if (false && owner != _index)",
     "LatePrb_IsNotCreditedToTheNextPoint"),

]


# 只跑指定几组（调试注入脚本本身用）：INJ_ONLY=A7,B4 python inject_verify.py
_only = os.environ.get("INJ_ONLY", "").strip()
if _only:
    _keys = [k.strip() for k in _only.split(",") if k.strip()]
    INJECTIONS = [x for x in INJECTIONS if any(k in x[0] for k in _keys)]


def run_tests():
    env = dict(os.environ)
    env["INKPEN_CI"] = "1"
    env["VSLANG"] = "1033"
    p = subprocess.Popen([TESTBAT], cwd=os.path.join(ROOT, "build"),
                         stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                         env=env, shell=True)
    out, _ = p.communicate()
    txt = out.decode("utf-8", "replace")
    return txt


def failed_names(txt):
    return set(re.findall(r"Failed\s+(\w+)", txt))


def main():
    os.makedirs(BAK, exist_ok=True)
    results = []

    for name, rel, good, bad, expect in INJECTIONS:
        path = os.path.join(ROOT, rel)
        orig = rd(path)
        if good not in orig:
            print("[SKIP] %s：找不到待注入片段（文件已变）" % name)
            results.append((name, "SKIP", ""))
            continue

        backup = os.path.join(BAK, os.path.basename(path) + "." + str(len(results)))
        shutil.copy2(path, backup)
        before = md5(path)

        wr(path, orig.replace(good, bad, 1))
        try:
            txt = run_tests()
            fails = failed_names(txt)
            hit = expect in fails
            mark = "OK  " if hit else "MISS"
            print("[%s] %-22s 捕获: %s" % (mark, name, ", ".join(sorted(fails)[:4]) or "无失败"))
            results.append((name, mark.strip(), ",".join(sorted(fails)[:4])))
        finally:
            shutil.copy2(backup, path)
            if md5(path) != before:
                print("[FATAL] 还原失败: " + path)
                sys.exit(2)

    print("\n=== 注入验证汇总 ===")
    ok = sum(1 for r in results if r[1] == "OK")
    print("命中 %d / %d" % (ok, len([r for r in results if r[1] != "SKIP"])))
    for n, m, f in results:
        print("  %-6s %-24s %s" % (m, n, f))
    return 0 if ok == len([r for r in results if r[1] != "SKIP"]) else 1


if __name__ == "__main__":
    sys.exit(main())
