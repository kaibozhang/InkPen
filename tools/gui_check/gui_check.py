# -*- coding: utf-8 -*-
"""InkPen 界面自动化验收（无人工目视）。

三段，各自起一个干净进程，互不影响：
  1) smoke   主窗能起来、控件齐全、无错误对话框、画布真的画出墨迹（像素统计）
  2) interact 真点「把格线写入 GCode」+「生成 GCode」，确认 GCode 行数真的变多
  3) config  真点「保存配置」，检查落盘 JSON（无 BOM、三个键），再点「读取配置」

只用 Win32 消息（EnumWindows / BM_CLICK），不需要鼠标位置，可无人值守重复跑。
依赖：Python 3.8+、Pillow（仅 smoke 用）。
用法：python tools\\gui_check\\gui_check.py [smoke|interact|config|all]
"""
import ctypes
import ctypes.wintypes as wt
import json
import os
import re
import subprocess
import sys
import time

u = ctypes.windll.user32
EnumProc = ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)
BM_CLICK = 0x00F5

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
EXE = os.path.join(ROOT, "dist", "InkPen.exe")
CFG = os.path.join(ROOT, "dist", "config", "config.json")
SHOT = os.path.join(ROOT, "docs", "gui_smoke.png")

MAIN_MARK = u"\u5199\u5b57\u673a\u5de5\u4f5c\u53f0"   # 写字机工作台


# ---------------- Win32 helpers ----------------

def text_of(h):
    n = u.GetWindowTextLengthW(h)
    if n <= 0:
        return ""
    b = ctypes.create_unicode_buffer(n + 1)
    u.GetWindowTextW(h, b, n + 1)
    return b.value


def cls_of(h):
    b = ctypes.create_unicode_buffer(256)
    u.GetClassNameW(h, b, 256)
    return b.value


def top_windows():
    out = []

    def cb(h, l):
        t = text_of(h)
        if t:
            out.append((h, t))
        return True

    u.EnumWindows(EnumProc(cb), 0)
    return out


def children(h):
    out = []

    def cb(c, l):
        out.append((c, cls_of(c), text_of(c)))
        return True

    u.EnumChildWindows(h, EnumProc(cb), 0)
    return out


def find_main(timeout=25.0):
    end = time.time() + timeout
    while time.time() < end:
        for h, t in top_windows():
            if MAIN_MARK in t:
                return h, t
        time.sleep(0.3)
    return None, None


def click(main, label, exact=False):
    for h, cls, t in children(main):
        if "BUTTON" not in cls:
            continue
        if (t == label) if exact else (label in t):
            u.SendMessageW(h, BM_CLICK, 0, 0)
            return True
    return False


def error_dialogs():
    # 主窗标题含「写字机工作台」，错误框标题恰好是 "InkPen"，必须精确匹配
    return [t for _, t in top_windows() if t.strip() == "InkPen"]


def info_text(main):
    for _, cls, t in children(main):
        if "STATIC" in cls and re.match(r"^\u9875 \d+", t):   # 页 N
            return t
    return ""


def gcode_lines(main):
    m = re.search(r"GCode (\d+) \u884c", info_text(main))     # GCode N 行
    return int(m.group(1)) if m else -1


def launch():
    if not os.path.exists(EXE):
        print("ERROR: %s not found, run build\\build.bat first" % EXE)
        sys.exit(2)
    p = subprocess.Popen([EXE])
    main, title = find_main()
    if main is None:
        print("RESULT=FAIL reason=main window not found")
        print("TITLES=", [t for _, t in top_windows()][:10])
        p.terminate()
        sys.exit(1)
    u.SetForegroundWindow(main)
    time.sleep(1.5)
    return p, main, title


def paper_centering(img):
    """量画布里的纸张左右留白是否对称。

    画布背景固定 (240,242,246)，纸张是纯白。纸张若被裁到画布边上，说明 Fit() 用了
    过期的 Size（历史上 SetJob 时窗体还没布局完，panX 偏大，纸跑到右边被切掉）。
    返回 (left_gap, right_gap, paper_w, canvas_w)。
    """
    w, h = img.size
    BG = (240, 242, 246)
    px = img.load()

    # 画布横向范围：背景色出现的最左/最右列
    cols = [x for x in range(w) if any(px[x, y] == BG for y in range(60, h - 60, 7))]
    if not cols:
        return None
    cx0, cx1 = cols[0], cols[-1]

    # 纸张横向范围：画布内的纯白像素
    whites = [x for x in range(cx0, cx1 + 1)
              if sum(1 for y in range(60, h - 60, 5) if px[x, y] == (255, 255, 255)) > 3]
    if not whites:
        return None
    p0, p1 = whites[0], whites[-1]
    return (p0 - cx0, cx1 - p1, p1 - p0, cx1 - cx0)


# ---------------- 1) smoke ----------------

def check_smoke():
    from PIL import ImageGrab

    p, main, title = launch()
    try:
        dialogs = error_dialogs()
        kids = [t for _, _, t in children(main)]

        rect = wt.RECT()
        u.GetWindowRect(main, ctypes.byref(rect))
        w, h = rect.right - rect.left, rect.bottom - rect.top

        img = ImageGrab.grab(bbox=(rect.left, rect.top, rect.right, rect.bottom))
        os.makedirs(os.path.dirname(SHOT), exist_ok=True)
        img.save(SHOT)

        must = [u"\u8f93\u5165\u6587\u7a3f",                 # 输入文稿
                u"\u7a3f\u7eb8\u6a21\u677f",                 # 稿纸模板
                u"\u751f\u6210 GCode / \u9884\u89c8",         # 生成 GCode / 预览
                u"\u5bfc\u51fa GCode \u6587\u4ef6",           # 导出 GCode 文件
                u"\u5f00\u59cb\u4e66\u5199",                 # 开始书写
                u"\u8fde\u63a5",                             # 连接
                u"\u5b9e\u65f6\u72b6\u6001 DRO",             # 实时状态 DRO
                u"\u624b\u52a8 jog / \u56de\u96f6"]           # 手动 jog / 回零
        missing = [m for m in must if not any(m in k for k in kids)]

        # 画布墨迹：中栏应有深色像素（说明笔画真被画出来了，不是空白面板）
        mid = img.crop((int(w * 0.28), int(h * 0.12), int(w * 0.78), int(h * 0.82))).convert("L")
        dark = sum(1 for px in mid.getdata() if px < 110)
        ratio = dark / float(mid.width * mid.height)

        cen = paper_centering(img.convert("RGB"))

        print("MAIN_TITLE=", title)
        print("SIZE=", w, "x", h)
        print("ERROR_DIALOGS=", dialogs)
        print("CHILD_COUNT=", len(kids))
        print("MISSING_CONTROLS=", missing)
        print("CANVAS_DARK_PIXELS=", dark, "RATIO=%.4f" % ratio)
        if cen is None:
            print("PAPER_CENTER= not measurable")
        else:
            lg, rg, pw, cw = cen
            print("PAPER_CENTER= left_gap=%d right_gap=%d paper_w=%d canvas_w=%d" % (lg, rg, pw, cw))
        print("SCREENSHOT=", SHOT)

        ok = (not missing) and (not dialogs) and w > 900 and ratio > 0.002
        # 纸张必须完整落在画布内、且左右留白基本对称
        if cen is None:
            ok = False
        else:
            lg, rg, pw, cw = cen
            if min(lg, rg) < 8 or abs(lg - rg) > max(24, pw * 0.10):
                print("PAPER_CENTER= OFF-CENTER/CLIPPED")
                ok = False
        print("RESULT=", "PASS" if ok else "FAIL")
        return ok
    finally:
        time.sleep(0.2)
        p.terminate()


# ---------------- 2) interact ----------------

def check_interact():
    p, main, _ = launch()
    try:
        before = info_text(main)
        n_before = gcode_lines(main)
        print("BEFORE:", before)

        ok_check = click(main, u"\u628a\u683c\u7ebf\u5199\u5165 GCode")   # 把格线写入 GCode
        ok_gen = click(main, u"\u751f\u6210 GCode")                       # 生成 GCode
        time.sleep(2.0)

        n_after = gcode_lines(main)
        print("CHECKBOX_CLICKED=", ok_check)
        print("GENERATE_CLICKED=", ok_gen)
        print("AFTER:", info_text(main))

        # 勾上格线后应多出格线笔画与对应 GCode 行
        ok = ok_check and ok_gen and n_after > n_before > 0
        print("GCODE_LINES_BEFORE=", n_before, "AFTER=", n_after)
        print("RESULT=", "PASS" if ok else "FAIL")
        return ok
    finally:
        time.sleep(0.2)
        p.terminate()


# ---------------- 3) config ----------------

def check_config():
    if os.path.exists(CFG):
        os.remove(CFG)

    p, main, _ = launch()
    try:
        saved = click(main, u"\u4fdd\u5b58\u914d\u7f6e", True)     # 保存配置
        time.sleep(1.0)

        exists = os.path.exists(CFG)
        has_bom = True
        keys = []
        if exists:
            raw = open(CFG, "rb").read()
            has_bom = raw.startswith(b"\xef\xbb\xbf")
            keys = sorted(json.loads(raw.decode("utf-8-sig")).keys())

        reloaded = click(main, u"\u8bfb\u53d6\u914d\u7f6e", True)   # 读取配置
        time.sleep(0.8)
        dialogs = error_dialogs()

        print("SAVE_CLICKED=", saved)
        print("CONFIG_FILE_EXISTS=", exists)
        print("HAS_BOM=", has_bom)
        print("KEYS=", keys)
        print("RELOAD_CLICKED=", reloaded)
        print("ERROR_DIALOGS=", dialogs)

        ok = (saved and exists and not has_bom and not dialogs and reloaded
              and "template" in keys and "machine" in keys and "text" in keys)
        print("RESULT=", "PASS" if ok else "FAIL")
        return ok
    finally:
        time.sleep(0.2)
        p.terminate()


# ---------------- main ----------------

CHECKS = {"smoke": check_smoke, "interact": check_interact, "config": check_config}

if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "all"
    names = list(CHECKS) if mode == "all" else [mode]
    bad = []
    for name in names:
        if name not in CHECKS:
            print("unknown check:", name)
            sys.exit(2)
        print("=== %s ===" % name)
        try:
            if not CHECKS[name]():
                bad.append(name)
        except Exception as ex:
            print("EXCEPTION:", repr(ex))
            bad.append(name)
    print("=== GUI CHECK SUMMARY === total=%d failed=%d %s"
          % (len(names), len(bad), ("failed=" + ",".join(bad)) if bad else ""))
    sys.exit(1 if bad else 0)
