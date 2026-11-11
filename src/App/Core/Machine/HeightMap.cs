using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace InkPen.Core
{
    /// <summary>
    /// 纸面高度图：网格探测得到的 Z 偏差表，用双线性插值做逐点 Z 补偿。
    ///
    /// 坐标系：一律「纸面 mm」，原点在纸左下、Y 向上，与排版 / 画布同一套坐标。
    ///         不要把机器坐标存进来——换原点角或反转轴之后高度图就全错了。
    /// Z 语义：毫米偏移，正值表示该处纸面比探测基准（第一点接触位）更高，笔要跟着抬。
    ///         未启用 / 数据不完整时 At() 恒返回 0，调用方无需再判空。
    /// </summary>
    public class HeightMap
    {
        public bool Enabled = false;
        public bool Valid = false;

        /// <summary>探测区域（纸面 mm）。区域外的点按边缘值夹取，不做外推。</summary>
        public double MinX, MinY, MaxX, MaxY;

        public int Cols = 3;
        public int Rows = 3;

        /// <summary>行优先存储，Z[r * Cols + c]，r = 0 对应 MinY。</summary>
        public double[] Z = new double[9];

        /// <summary>探测基准 Z（机床坐标，第一点接触时的 PRBZ）。仅作显示/回填参考。</summary>
        public double BaseZ = 0;

        /// <summary>弹簧笔架压入量：接触后再往下压这么多，靠弹力保持接触。</summary>
        public double PressDepthMm = 0.3;

        public int FailCount = 0;
        public string ProbedAt = "";

        public int Count { get { return Cols * Rows; } }

        /// <summary>
        /// 数据本身可不可以拿去插值。
        /// 刻意**不**看 Enabled：开关归 MachineConfig.UseHeightMap 管，
        /// 这里再判一次会出现「读回高度图后勾选框刷新把 Enabled 清掉、热力图变空白」的连锁 bug。
        /// </summary>
        public bool IsUsable
        {
            get
            {
                return Valid && Cols >= 2 && Rows >= 2
                    && Z != null && Z.Length >= Cols * Rows
                    && MaxX > MinX && MaxY > MinY;
            }
        }

        public void Reset(int cols, int rows)
        {
            Cols = Math.Max(2, Math.Min(64, cols));
            Rows = Math.Max(2, Math.Min(64, rows));
            Z = new double[Cols * Rows];
            Valid = false;
            FailCount = 0;
        }

        public double Get(int c, int r)
        {
            if (Z == null) return 0;
            int i = r * Cols + c;
            if (i < 0 || i >= Z.Length) return 0;
            return Z[i];
        }

        public void Set(int c, int r, double z)
        {
            if (Z == null) return;
            int i = r * Cols + c;
            if (i < 0 || i >= Z.Length) return;
            Z[i] = z;
        }

        /// <summary>
        /// 双线性插值。区域外夹取到边界（不外推），避免纸边出现虚假的高差台阶。
        /// </summary>
        public double At(double x, double y)
        {
            if (!IsUsable) return 0;

            double u = (x - MinX) / (MaxX - MinX) * (Cols - 1);
            double v = (y - MinY) / (MaxY - MinY) * (Rows - 1);
            if (double.IsNaN(u) || double.IsNaN(v)) return 0;

            // 先夹取再取整，防止 (int) 对负数截断成 0 造成边界行被重复采样
            if (u < 0) u = 0; else if (u > Cols - 1) u = Cols - 1;
            if (v < 0) v = 0; else if (v > Rows - 1) v = Rows - 1;

            int c0 = (int)Math.Floor(u);
            int r0 = (int)Math.Floor(v);
            if (c0 > Cols - 2) c0 = Cols - 2;
            if (r0 > Rows - 2) r0 = Rows - 2;
            if (c0 < 0) c0 = 0;
            if (r0 < 0) r0 = 0;

            double fx = u - c0;
            double fy = v - r0;

            double z00 = Get(c0, r0);
            double z10 = Get(c0 + 1, r0);
            double z01 = Get(c0, r0 + 1);
            double z11 = Get(c0 + 1, r0 + 1);

            double a = z00 + (z10 - z00) * fx;
            double b = z01 + (z11 - z01) * fx;
            return a + (b - a) * fy;
        }

        /// <summary>3×3 均值平滑，去掉单点探测毛刺（探针抖动、纸面小颗粒）。</summary>
        public void Smooth()
        {
            if (Z == null || Cols < 3 || Rows < 3) return;
            // 只平滑**内部**点：边界点的邻域在纸外，取平均等于拿半边数据外推。
            // 更关键的是「网格只有 2 行/2 列」时（探测页的行列下限就是 2），
            // 3×3 均值会把每个点都变成全图平均 —— 单向倾斜被彻底抹平，
            // 起伏显示 0.000 mm，用户以为纸是平的，于是整幅笔压都是错的。
            // 小网格没有任何冗余可供平滑，直接跳过。
            double[] src = (double[])Z.Clone();
            for (int r = 1; r < Rows - 1; r++)
            {
                for (int c = 1; c < Cols - 1; c++)
                {
                    double sum = 0;
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                            sum += src[(r + dr) * Cols + (c + dc)];
                    Z[r * Cols + c] = sum / 9.0;
                }
            }
            // 线性倾斜场在内部取 3×3 均值后不变（等于中心值），边界又保持原值，
            // 所以倾斜与真实起伏都留得住，只把孤立的单点毛刺削掉。
        }

        public double MinZ()
        {
            if (Z == null || Z.Length == 0) return 0;
            double m = Z[0];
            for (int i = 1; i < Z.Length; i++) if (Z[i] < m) m = Z[i];
            return m;
        }

        public double MaxZ()
        {
            if (Z == null || Z.Length == 0) return 0;
            double m = Z[0];
            for (int i = 1; i < Z.Length; i++) if (Z[i] > m) m = Z[i];
            return m;
        }

        public double Mean()
        {
            if (Z == null || Z.Length == 0) return 0;
            double s = 0;
            for (int i = 0; i < Z.Length; i++) s += Z[i];
            return s / Z.Length;
        }

        /// <summary>把整体平移到「基准 0」：减去均值。探测绝对值没有意义，相对起伏才有。</summary>
        public void Normalize()
        {
            double mean = Mean();
            if (Z == null) return;
            for (int i = 0; i < Z.Length; i++) Z[i] -= mean;
            BaseZ += mean;
        }

        public string ToJson()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["enabled"] = Enabled;
            d["valid"] = Valid;
            d["minX"] = MinX;
            d["minY"] = MinY;
            d["maxX"] = MaxX;
            d["maxY"] = MaxY;
            d["cols"] = (double)Cols;
            d["rows"] = (double)Rows;
            d["baseZ"] = BaseZ;
            d["pressDepthMm"] = PressDepthMm;
            d["failCount"] = (double)FailCount;
            d["probedAt"] = ProbedAt ?? "";
            List<object> zs = new List<object>();
            if (Z != null) foreach (double z in Z) zs.Add(z);
            d["z"] = zs;
            return Json.Stringify(d);
        }

        public static HeightMap FromJson(string json)
        {
            HeightMap m = new HeightMap();
            Dictionary<string, object> d = Json.Obj(Json.Parse(json));
            if (d == null) return m;

            m.Enabled = Json.Bool(d, "enabled", false);
            m.MinX = Json.Num(d, "minX", 0);
            m.MinY = Json.Num(d, "minY", 0);
            m.MaxX = Json.Num(d, "maxX", 0);
            m.MaxY = Json.Num(d, "maxY", 0);
            m.Cols = (int)Json.Num(d, "cols", 3);
            m.Rows = (int)Json.Num(d, "rows", 3);
            m.BaseZ = Json.Num(d, "baseZ", 0);
            m.PressDepthMm = Json.Num(d, "pressDepthMm", 0.3);
            m.FailCount = (int)Json.Num(d, "failCount", 0);
            m.ProbedAt = Json.Str(d, "probedAt", "");

            object zv;
            List<object> zs = (d.TryGetValue("z", out zv)) ? zv as List<object> : null;
            // Reset() 会把 Valid 清成 false，所以「是否有效」必须在 Reset 之后再落实，
            // 否则读回来的高度图永远是无效的（界面显示「未启用」，补偿被静默跳过）。
            bool valid = Json.Bool(d, "valid", false);
            int cols = m.Cols, rows = m.Rows;
            m.Reset(cols, rows);
            if (zs != null)
            {
                for (int i = 0; i < zs.Count && i < m.Z.Length; i++)
                {
                    object o = zs[i];
                    if (o is double) m.Z[i] = (double)o;
                }
            }
            // 数组长度与行列不符时视为脏数据，不能拿去补偿
            m.Valid = valid && zs != null && zs.Count == cols * rows;
            return m;
        }

        public string Summary()
        {
            if (!IsUsable) return "高度图未启用";
            return "高度图 " + Cols + "×" + Rows
                 + " · 起伏 " + (MaxZ() - MinZ()).ToString("F3", CultureInfo.InvariantCulture) + " mm"
                 + " · 均 " + Mean().ToString("F3", CultureInfo.InvariantCulture) + " mm"
                 + (FailCount > 0 ? " · " + FailCount + " 点失败" : "");
        }
    }
}
