# -*- coding: utf-8 -*-
"""把编译清单里的绝对路径换成相对路径，并固定源码编码为 UTF-8。

绝对路径 `D:\\Writer\\InkPen\\...` 会让整条 csc 链只能在作者这台机器上跑：
换目录、换盘符、或者把仓库克隆到含空格的路径下，构建直接失败。
csc 解析 rsp 时相对路径以当前工作目录为准，所以两个 .bat 都先 pushd 到项目根。
"""
import io
import os

ROOT = r"D:\Writer\InkPen"
ABS = "D:\\Writer\\InkPen\\"


def load(p):
    return io.open(p, encoding="utf-8").read()


def save(p, s):
    io.open(p, "w", encoding="utf-8", newline="\r\n").write(s)


for name in ("sources.rsp", "selftest.rsp"):
    path = os.path.join(ROOT, "build", name)
    s = load(path)
    before = s.count(ABS)
    s = s.replace(ABS, "")
    # /out 与 /recurse 也是绝对路径；相对化后同样以 cwd 为准
    s = s.replace("/out:dist" + "\\", "/out:dist\\")   # 幂等
    if "/codepage:65001" not in s:
        s = s.replace("/langversion:5", "/langversion:5\n/codepage:65001")
    if not s.endswith("\n"):
        s += "\r\n"
    save(path, s)
    print("%-12s 替换绝对路径 %d 处" % (name, before))

# 校验 /out 行
for name in ("sources.rsp", "selftest.rsp"):
    for ln in load(os.path.join(ROOT, "build", name)).splitlines():
        if ln.startswith("/out") or ln.startswith("/recurse"):
            print(name, "->", ln)
