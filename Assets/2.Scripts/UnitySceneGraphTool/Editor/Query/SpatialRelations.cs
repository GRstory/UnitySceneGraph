using UnityEngine;

namespace SceneGraph.Editor
{
    public enum RelationType { On, Under, Inside, Contains, Near, Above, Below, LeftOf, RightOf, InFrontOf, Behind }

    /// <summary>
    /// bounds 기반 관계 판정. PLAN.md 4.1. A = target, B = object. 상자 하나 대 하나.
    /// contains의 계층 조건, on/above/below의 받침 파츠 열거는 QueryEngine이 한다.
    /// </summary>
    public static class SpatialRelations
    {
        public static bool TryParse(string name, out RelationType type)
        {
            switch (name)
            {
                case "on": type = RelationType.On; return true;
                case "under": type = RelationType.Under; return true;
                case "inside": type = RelationType.Inside; return true;
                case "contains": type = RelationType.Contains; return true;
                case "near": type = RelationType.Near; return true;
                case "above": type = RelationType.Above; return true;
                case "below": type = RelationType.Below; return true;
                case "left_of": type = RelationType.LeftOf; return true;
                case "right_of": type = RelationType.RightOf; return true;
                case "in_front_of": type = RelationType.InFrontOf; return true;
                case "behind": type = RelationType.Behind; return true;
                default: type = default; return false;
            }
        }

        public static string Name(RelationType type)
        {
            switch (type)
            {
                case RelationType.LeftOf: return "left_of";
                case RelationType.RightOf: return "right_of";
                case RelationType.InFrontOf: return "in_front_of";
                default: return type.ToString().ToLowerInvariant();
            }
        }

        public static bool IsAxisDependent(RelationType type) =>
            type == RelationType.LeftOf || type == RelationType.RightOf || type == RelationType.InFrontOf || type == RelationType.Behind;

        /// <summary>
        /// tol = tolFactor × min(A.size.y, B.size.y). 평면(size.y = 0) 때문에 0이 되면 tolEpsilon으로 막는다.
        /// tolEpsilon은 0 나누기/0 허용폭 방어일 뿐 PLAN.md 미결인 절대 하한이 아니다.
        /// </summary>
        public static float Tolerance(Bounds a, Bounds b, RelationOptions o) =>
            Mathf.Max(o.tolFactor * Mathf.Min(a.size.y, b.size.y), o.tolEpsilon);

        public static bool Holds(RelationType type, Bounds a, Bounds b, Frame frame, RelationOptions o)
        {
            var tol = Tolerance(a, b, o);
            if (IsAxisDependent(type)) return HoldsAxisRelation(type, a, b, frame, tol);
            return HoldsBoxRelation(type, a, b, o, tol);
        }

        static bool HoldsBoxRelation(RelationType type, Bounds a, Bounds b, RelationOptions o, float tol)
        {
            switch (type)
            {
                case RelationType.On:
                    // 위로 뜨는 건 tol까지, 아래로 박히는 건 A 높이의 onPenetrationRatio까지
                    var gap = a.min.y - b.max.y;
                    return gap <= tol && gap >= -o.onPenetrationRatio * a.size.y && FootprintOverlap(a, b) >= o.onFootprintOverlap;
                case RelationType.Under:
                    return a.max.y <= b.max.y && InFootprint(a.center, b) && a.center.y < b.center.y;
                case RelationType.Inside:
                    return ContainedFraction(a, b) >= o.insideRatio;
                case RelationType.Contains:
                    return ContainedFraction(b, a) >= o.insideRatio;
                case RelationType.Near:
                    return Vector3.Distance(a.center, b.center) <= Mathf.Max(a.size.magnitude, b.size.magnitude) * o.nearFactor;
                case RelationType.Above:
                    return FootprintOverlap(a, b) > 0f && a.min.y - b.max.y > tol;
                case RelationType.Below:
                    return FootprintOverlap(a, b) > 0f && b.min.y - a.max.y > tol;
            }
            return false;
        }

        static bool HoldsAxisRelation(RelationType type, Bounds a, Bounds b, Frame frame, float tol)
        {
            var d = a.center - b.center;
            var dr = Vector3.Dot(d, frame.Right);
            var df = Vector3.Dot(d, frame.Forward);
            var du = Vector3.Dot(d, frame.Up);
            var ar = Mathf.Abs(dr);
            var af = Mathf.Abs(df);
            var au = Mathf.Abs(du);
            switch (type)
            {
                case RelationType.LeftOf: return dr < -tol && ar >= af && ar >= au;
                case RelationType.RightOf: return dr > tol && ar >= af && ar >= au;
                case RelationType.InFrontOf: return df > tol && af >= ar && af >= au;
                case RelationType.Behind: return df < -tol && af >= ar && af >= au;
            }
            return false;
        }

        /// <summary>A의 XZ 발자국 중 B와 겹치는 비율 (A 기준). A 발자국이 면적 0이면 축별로 안/밖.</summary>
        public static float FootprintOverlap(Bounds a, Bounds b) =>
            AxisFraction(a.min.x, a.max.x, b.min.x, b.max.x) * AxisFraction(a.min.z, a.max.z, b.min.z, b.max.z);

        /// <summary>inner 부피 중 outer 안에 든 비율. 두께 0인 축은 안/밖으로 본다.</summary>
        public static float ContainedFraction(Bounds inner, Bounds outer) =>
            AxisFraction(inner.min.x, inner.max.x, outer.min.x, outer.max.x) *
            AxisFraction(inner.min.y, inner.max.y, outer.min.y, outer.max.y) *
            AxisFraction(inner.min.z, inner.max.z, outer.min.z, outer.max.z);

        static bool InFootprint(Vector3 p, Bounds b) =>
            p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z;

        static float AxisFraction(float aMin, float aMax, float bMin, float bMax)
        {
            var len = aMax - aMin;
            if (len <= 1e-6f) return aMin >= bMin && aMin <= bMax ? 1f : 0f;
            var overlap = Mathf.Min(aMax, bMax) - Mathf.Max(aMin, bMin);
            return Mathf.Clamp01(overlap / len);
        }
    }
}
