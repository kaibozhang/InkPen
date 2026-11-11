using System;
using System.Collections.Generic;
using InkPen.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InkPen.Tests
{
    [TestClass]
    public class GeometryTests
    {
        private static MachineConfig Paper()
        {
            MachineConfig m = new MachineConfig();
            m.PaperWidthMm = 210;
            m.PaperHeightMm = 297;
            return m;
        }

        [TestMethod]
        public void CoordTransform_BottomLeft_IsIdentity()
        {
            MachineConfig m = Paper();
            Pt p = CoordTransform.ToMachine(new Pt(10, 20), m);
            Assert.AreEqual(10.0, p.X, 1e-9);
            Assert.AreEqual(20.0, p.Y, 1e-9);
        }

        [TestMethod]
        public void CoordTransform_EachOriginCorner_MapsAsExpected()
        {
            MachineConfig m = Paper();

            m.Origin = OriginCorner.TopLeft;
            Pt tl = CoordTransform.ToMachine(new Pt(10, 20), m);
            Assert.AreEqual(10.0, tl.X, 1e-9);
            Assert.AreEqual(277.0, tl.Y, 1e-9);

            m.Origin = OriginCorner.BottomRight;
            Pt br = CoordTransform.ToMachine(new Pt(10, 20), m);
            Assert.AreEqual(200.0, br.X, 1e-9);
            Assert.AreEqual(20.0, br.Y, 1e-9);

            m.Origin = OriginCorner.TopRight;
            Pt tr = CoordTransform.ToMachine(new Pt(10, 20), m);
            Assert.AreEqual(200.0, tr.X, 1e-9);
            Assert.AreEqual(277.0, tr.Y, 1e-9);
        }

        [TestMethod]
        public void CoordTransform_InvertFlags_NegateAxis()
        {
            MachineConfig m = Paper();
            m.InvertX = true;
            m.InvertY = true;
            Pt p = CoordTransform.ToMachine(new Pt(10, 20), m);
            Assert.AreEqual(-10.0, p.X, 1e-9);
            Assert.AreEqual(-20.0, p.Y, 1e-9);
        }

        [TestMethod]
        public void Rdp_KeepsEndpoints_AndRemovesCollinearNoise()
        {
            List<Pt> pts = new List<Pt>();
            for (int i = 0; i <= 20; i++) pts.Add(new Pt(i, (i % 2 == 0) ? 0.0 : 0.01));

            List<Pt> s = Rdp.Simplify(pts, 0.1);

            Assert.AreEqual(2, s.Count, "接近直线的点应被裁剪到只剩首尾");
            Assert.AreEqual(pts[0].X, s[0].X, 1e-9);
            Assert.AreEqual(pts[pts.Count - 1].X, s[s.Count - 1].X, 1e-9);
        }

        [TestMethod]
        public void Rdp_PreservesSharpCorner()
        {
            List<Pt> pts = new List<Pt>();
            pts.Add(new Pt(0, 0));
            pts.Add(new Pt(5, 5));
            pts.Add(new Pt(10, 0));
            pts.Add(new Pt(15, 0));

            List<Pt> s = Rdp.Simplify(pts, 1.0);

            Assert.IsTrue(s.Count >= 3, "尖角不应被抹平");
            bool hasApex = false;
            foreach (Pt p in s) if (Math.Abs(p.X - 5) < 1e-9 && Math.Abs(p.Y - 5) < 1e-9) hasApex = true;
            Assert.IsTrue(hasApex, "顶点 (5,5) 应保留");
        }

        [TestMethod]
        public void Rdp_NullOrZeroEps_IsSafe()
        {
            Assert.AreEqual(0, Rdp.Simplify(null, 0.1).Count);
            List<Pt> two = new List<Pt>();
            two.Add(new Pt(0, 0));
            two.Add(new Pt(1, 1));
            Assert.AreEqual(2, Rdp.Simplify(two, 0.1).Count);
            Assert.AreEqual(2, Rdp.Simplify(two, 0).Count);
        }

        [TestMethod]
        public void Hash_IsDeterministic_AndBounded()
        {
            for (int i = 0; i < 500; i++)
            {
                double v = HandwritingPerturb.Hash(i, i + 1, i + 2, 20261110);
                Assert.AreEqual(v, HandwritingPerturb.Hash(i, i + 1, i + 2, 20261110), 0.0);
                Assert.IsTrue(v >= -1.0 && v <= 1.0, "扰动值必须落在 [-1,1]，实际 " + v);
            }
        }

        [TestMethod]
        public void Hash_DifferentSeed_ChangesResult()
        {
            bool differs = false;
            for (int i = 0; i < 50; i++)
                if (Math.Abs(HandwritingPerturb.Hash(i, 1, 2, 1) - HandwritingPerturb.Hash(i, 1, 2, 2)) > 1e-9)
                    differs = true;
            Assert.IsTrue(differs, "换种子应产生不同扰动");
        }

        [TestMethod]
        public void EnhanceTail_ZeroStrength_ReturnsSameReference()
        {
            List<Pt> pts = new List<Pt>();
            pts.Add(new Pt(0, 0));
            pts.Add(new Pt(10, 0));
            Assert.AreSame(pts, HandwritingPerturb.EnhanceTail(pts, 0));
        }

        [TestMethod]
        public void EnhanceTail_AddsEntryAndExitMarks()
        {
            List<Pt> pts = new List<Pt>();
            pts.Add(new Pt(0, 0));
            pts.Add(new Pt(10, 0));
            pts.Add(new Pt(20, 0));

            List<Pt> r = HandwritingPerturb.EnhanceTail(pts, 0.1);

            Assert.AreEqual(5, r.Count, "三点的笔画应变成五点（首尾各加一个回描点）");
            Assert.IsTrue(r[0].X < 0, "起笔回描应在起点之前");
            Assert.IsTrue(r[r.Count - 1].X > 20, "收笔回描应在终点之后");
        }

        [TestMethod]
        public void Rng_SameSeed_SameSequence()
        {
            Rng a = new Rng(12345);
            Rng b = new Rng(12345);
            for (int i = 0; i < 100; i++) Assert.AreEqual(a.Next(), b.Next());
        }
    }
}
