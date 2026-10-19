using NUnit.Framework;
using UnityEngine;

namespace SceneGraph.Editor.Tests
{
    /// <summary>PLAN.md 4.1 관계 11종과 frame 변환.</summary>
    public class SpatialRelationsTests
    {
        static readonly RelationOptions Options = new RelationOptions();
        static readonly Bounds Desk = MinMax(new Vector3(-0.6f, 0f, -0.3f), new Vector3(0.6f, 0.8f, 0.3f));

        static Bounds MinMax(Vector3 min, Vector3 max)
        {
            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }

        static Bounds Box(Vector3 center, float size) => new Bounds(center, Vector3.one * size);

        static bool Holds(RelationType type, Bounds a, Bounds b) => SpatialRelations.Holds(type, a, b, Frame.World, Options);

        [Test]
        public void On_TouchingTopWithFootprintOverlap()
        {
            Assert.IsTrue(Holds(RelationType.On, Box(new Vector3(0, 0.85f, 0), 0.1f), Desk));
            // 허용폭(0.1 × 0.1 = 0.01) 밖으로 떠 있음
            Assert.IsFalse(Holds(RelationType.On, Box(new Vector3(0, 0.87f, 0), 0.1f), Desk));
            // 발자국 겹침 < 0.5
            Assert.IsFalse(Holds(RelationType.On, Box(new Vector3(0.64f, 0.85f, 0), 0.1f), Desk));
        }

        [Test]
        public void On_AllowsPenetrationUpToHalfTargetHeight()
        {
            // A 높이 0.1, 받침 윗면 0.8. 0.04 박힘 → 성립, 0.06 박힘 → 불성립
            Assert.IsTrue(Holds(RelationType.On, Box(new Vector3(0, 0.81f, 0), 0.1f), Desk));
            Assert.IsFalse(Holds(RelationType.On, Box(new Vector3(0, 0.79f, 0), 0.1f), Desk));
        }

        [Test]
        public void Under_CenterInFootprintAndLower()
        {
            Assert.IsTrue(Holds(RelationType.Under, Box(new Vector3(0, 0.1f, 0), 0.2f), Desk));
            Assert.IsFalse(Holds(RelationType.Under, Box(new Vector3(1.5f, 0.1f, 0), 0.2f), Desk));
            Assert.IsFalse(Holds(RelationType.Under, Box(new Vector3(0, 0.85f, 0), 0.1f), Desk));
        }

        [Test]
        public void Inside_AtLeast80Percent()
        {
            var drawer = MinMax(new Vector3(-0.5f, 0.5f, -0.25f), new Vector3(0.5f, 0.7f, 0.25f));
            Assert.IsTrue(Holds(RelationType.Inside, Box(new Vector3(0, 0.6f, 0), 0.1f), drawer));
            Assert.IsFalse(Holds(RelationType.Inside, Box(new Vector3(0.5f, 0.6f, 0), 0.1f), drawer));
        }

        [Test]
        public void Contains_IsInverseOfInsideForBounds()
        {
            var drawer = MinMax(new Vector3(-0.5f, 0.5f, -0.25f), new Vector3(0.5f, 0.7f, 0.25f));
            var pen = Box(new Vector3(0, 0.6f, 0), 0.1f);
            Assert.IsTrue(Holds(RelationType.Contains, drawer, pen));
            Assert.IsFalse(Holds(RelationType.Contains, pen, drawer));
        }

        [Test]
        public void Near_CenterDistanceWithinOneAndHalfDiagonals()
        {
            var a = Box(Vector3.zero, 1f); // 대각선 √3 ≈ 1.732 → 한계 ≈ 2.598
            Assert.IsTrue(Holds(RelationType.Near, a, Box(new Vector3(2.5f, 0, 0), 1f)));
            Assert.IsFalse(Holds(RelationType.Near, a, Box(new Vector3(2.7f, 0, 0), 1f)));
        }

        [Test]
        public void Above_And_Below_NeedOverlapAndGap()
        {
            var lamp = Box(new Vector3(0, 1.5f, 0), 0.2f);
            Assert.IsTrue(Holds(RelationType.Above, lamp, Desk));
            Assert.IsTrue(Holds(RelationType.Below, Desk, lamp));
            // 닿아 있으면 above 아님 (on)
            Assert.IsFalse(Holds(RelationType.Above, Box(new Vector3(0, 0.85f, 0), 0.1f), Desk));
            // XZ 겹침 없음
            Assert.IsFalse(Holds(RelationType.Above, Box(new Vector3(3, 1.5f, 0), 0.2f), Desk));
        }

        [Test]
        public void AxisRelations_WorldFrame()
        {
            var b = Box(Vector3.zero, 1f);
            Assert.IsTrue(Holds(RelationType.LeftOf, Box(new Vector3(-2, 0, 0.5f), 1f), b));
            Assert.IsTrue(Holds(RelationType.RightOf, Box(new Vector3(2, 0, 0.5f), 1f), b));
            Assert.IsTrue(Holds(RelationType.InFrontOf, Box(new Vector3(0.5f, 0, 2), 1f), b));
            Assert.IsTrue(Holds(RelationType.Behind, Box(new Vector3(0.5f, 0, -2), 1f), b));
            // |dr|이 최대가 아니면 left_of 아님
            Assert.IsFalse(Holds(RelationType.LeftOf, Box(new Vector3(-1, 0, 3), 1f), b));
            // tol 안이면 아님
            Assert.IsFalse(Holds(RelationType.RightOf, Box(new Vector3(0.05f, 0, 0), 1f), b));
        }

        [Test]
        public void Frame_WorldAxes()
        {
            Assert.IsTrue(Frame.TryWorld("+z", "+y", out var f));
            AssertVec(Vector3.right, f.Right);
            Assert.IsTrue(Frame.TryWorld("-z", "+y", out f));
            AssertVec(Vector3.left, f.Right);
            Assert.IsTrue(Frame.TryWorld("+x", "+y", out f));
            AssertVec(Vector3.back, f.Right);
            Assert.IsFalse(Frame.TryWorld("+y", "+y", out _));
            Assert.IsFalse(Frame.TryWorld("up", "+y", out _));
        }

        [Test]
        public void Frame_WorldMinusZ_FlipsLeftRight()
        {
            Assert.IsTrue(Frame.TryWorld("-z", "+y", out var f));
            var a = Box(new Vector3(2, 0, 0), 1f);
            var b = Box(Vector3.zero, 1f);
            Assert.IsTrue(SpatialRelations.Holds(RelationType.LeftOf, a, b, f, Options));
            Assert.IsFalse(SpatialRelations.Holds(RelationType.RightOf, a, b, f, Options));
        }

        [Test]
        public void Frame_ObjectTransform_UsesItsAxes()
        {
            var go = new GameObject("Sofa");
            try
            {
                go.transform.rotation = Quaternion.Euler(0, 90, 0); // forward = +x
                var f = Frame.FromTransform(go.transform);
                AssertVec(Vector3.right, f.Forward);
                AssertVec(Vector3.back, f.Right);
                var sofa = Box(Vector3.zero, 1f);
                Assert.IsTrue(SpatialRelations.Holds(RelationType.InFrontOf, Box(new Vector3(2, 0, 0), 0.5f), sofa, f, Options));
                Assert.IsTrue(SpatialRelations.Holds(RelationType.RightOf, Box(new Vector3(0, 0, -2), 0.5f), sofa, f, Options));
                // 같은 배치를 월드 frame으로 보면 앞이 아니라 오른쪽
                Assert.IsFalse(SpatialRelations.Holds(RelationType.InFrontOf, Box(new Vector3(2, 0, 0), 0.5f), sofa, Frame.World, Options));
                Assert.IsTrue(SpatialRelations.Holds(RelationType.RightOf, Box(new Vector3(2, 0, 0), 0.5f), sofa, Frame.World, Options));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Frame_CameraLikeTransform()
        {
            var go = new GameObject("Cam");
            try
            {
                go.transform.rotation = Quaternion.Euler(30, 180, 0); // 내려다보며 -z를 본다
                var f = Frame.FromTransform(go.transform);
                var b = Box(Vector3.zero, 1f);
                // 카메라 기준 오른쪽은 월드 -x
                Assert.IsTrue(SpatialRelations.Holds(RelationType.RightOf, Box(new Vector3(-2, 0, 0), 0.5f), b, f, Options));
                Assert.IsTrue(SpatialRelations.Holds(RelationType.LeftOf, Box(new Vector3(2, 0, 0), 0.5f), b, f, Options));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Tolerance_FlatPlaneDoesNotCollapseToZero()
        {
            var floor = MinMax(new Vector3(-5, 0, -5), new Vector3(5, 0, 5));
            var box = Box(new Vector3(0, 0.5f, 0), 1f);
            Assert.Greater(SpatialRelations.Tolerance(box, floor, Options), 0f);
            Assert.IsTrue(SpatialRelations.Holds(RelationType.On, box, floor, Frame.World, Options));
            Assert.AreEqual(1f, SpatialRelations.FootprintOverlap(floor, floor));
        }

        [Test]
        public void ParseNames()
        {
            foreach (var name in new[] { "on", "under", "inside", "contains", "near", "above", "below", "left_of", "right_of", "in_front_of", "behind" })
            {
                Assert.IsTrue(SpatialRelations.TryParse(name, out var type), name);
                Assert.AreEqual(name, SpatialRelations.Name(type));
            }
            Assert.IsFalse(SpatialRelations.TryParse("beside", out _));
        }

        static void AssertVec(Vector3 expected, Vector3 actual) =>
            Assert.Less(Vector3.Distance(expected, actual), 1e-4f, $"expected {expected} got {actual}");
    }
}
