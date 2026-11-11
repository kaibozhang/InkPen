"""HeightMap.At / Smooth 的复算（独立的双线性参考实现 + 3x3 均值平滑）。"""
import math
import random


def at(z, cols, rows, minx, miny, maxx, maxy, x, y):
    u = (x - minx) / (maxx - minx) * (cols - 1)
    v = (y - miny) / (maxy - miny) * (rows - 1)
    u = min(max(u, 0.0), cols - 1)
    v = min(max(v, 0.0), rows - 1)
    c0 = min(int(math.floor(u)), cols - 2)
    r0 = min(int(math.floor(v)), rows - 2)
    fx, fy = u - c0, v - r0
    g = lambda c, r: z[r * cols + c]
    a = g(c0, r0) + (g(c0 + 1, r0) - g(c0, r0)) * fx
    b = g(c0, r0 + 1) + (g(c0 + 1, r0 + 1) - g(c0, r0 + 1)) * fx
    return a + (b - a) * fy


def ref(z, cols, rows, minx, miny, maxx, maxy, x, y):
    """按「先算权重再加」的另一种写法，独立于上面的实现。"""
    w = maxx - minx
    h = maxy - miny
    tx = min(max((x - minx) / w, 0.0), 1.0) * (cols - 1)
    ty = min(max((y - miny) / h, 0.0), 1.0) * (rows - 1)
    c0 = min(int(tx), cols - 2)
    r0 = min(int(ty), rows - 2)
    ax, ay = tx - c0, ty - r0
    s = 0.0
    for (c, r, wgt) in ((c0, r0, (1 - ax) * (1 - ay)), (c0 + 1, r0, ax * (1 - ay)),
                        (c0, r0 + 1, (1 - ax) * ay), (c0 + 1, r0 + 1, ax * ay)):
        s += z[r * cols + c] * wgt
    return s


def smooth(z, cols, rows):
    src = list(z)
    out = list(z)
    for r in range(rows):
        for c in range(cols):
            s = 0.0
            n = 0
            for dr in (-1, 0, 1):
                for dc in (-1, 0, 1):
                    rr, cc = r + dr, c + dc
                    if rr < 0 or rr >= rows or cc < 0 or cc >= cols:
                        continue
                    s += src[rr * cols + cc]
                    n += 1
            out[r * cols + c] = s / n
    return out


print("[A] At() 与独立双线性实现的偏差（3x4 随机值，20000 个随机点 + 边界）")
random.seed(7)
cols, rows = 3, 4
z = [random.uniform(-2, 2) for _ in range(cols * rows)]
mx = 0.0
for _ in range(20000):
    x = random.uniform(-100, 400)
    y = random.uniform(-100, 400)
    mx = max(mx, abs(at(z, cols, rows, 10, 10, 200, 287, x, y)
                     - ref(z, cols, rows, 10, 10, 200, 287, x, y)))
print("   max |At - ref| = %.3e" % mx)

print()
print("[B] Smooth() 对 3x2 网格（只在 Y 方向倾斜 1mm）的影响")
z2 = [0.0, 0.0, 0.0, 1.0, 1.0, 1.0]     # rows=2, cols=3；r=0 -> y=min, r=1 -> y=max
print("   平滑前:", z2)
print("   平滑后:", ["%.3f" % v for v in smooth(z2, 3, 2)])
print("   At(0,ymin)=%.3f  At(0,ymax)=%.3f  → Y 向补偿差 %.3f mm"
      % (at(smooth(z2, 3, 2), 3, 2, 0, 0, 100, 100, 0, 0),
         at(smooth(z2, 3, 2), 3, 2, 0, 0, 100, 100, 0, 100),
         at(smooth(z2, 3, 2), 3, 2, 0, 0, 100, 100, 0, 0)
         - at(smooth(z2, 3, 2), 3, 2, 0, 0, 100, 100, 0, 100)))

print()
print("[C] Smooth() 对 2x2 网格（左下 0、右上 1 的整体倾斜）的影响")
z3 = [0.0, 1.0, 0.0, 1.0]
print("   平滑前:", z3, " 平滑后:", ["%.3f" % v for v in smooth(z3, 2, 2)])
print("   区域起伏(平滑前) = %.3f mm, 平滑后 = %.3f mm"
      % (max(z3) - min(z3), max(smooth(z3, 2, 2)) - min(smooth(z3, 2, 2))))

print()
print("[D] Smooth() 对 3x3 网格（右下高 1mm）的影响")
z4 = [0, 0, 1, 0, 0, 1, 0, 0, 1]
print("   平滑前:", z4)
print("   平滑后:", ["%.3f" % v for v in smooth(z4, 3, 3)])
