using UnityEngine;

namespace SceneGraph.Editor
{
    /// <summary>축 의존 관계의 기준. 단위 벡터 R, F, U.</summary>
    public readonly struct Frame
    {
        public readonly Vector3 Right, Forward, Up;

        public Frame(Vector3 right, Vector3 forward, Vector3 up)
        {
            Right = right.normalized;
            Forward = forward.normalized;
            Up = up.normalized;
        }

        public static Frame World => new Frame(Vector3.right, Vector3.forward, Vector3.up);

        /// <summary>{ "type": "world", "forward": "+z", "up": "+y" }. right = up × forward.</summary>
        public static bool TryWorld(string forward, string up, out Frame frame)
        {
            frame = default;
            if (!TryAxis(forward, out var f) || !TryAxis(up, out var u)) return false;
            if (Mathf.Abs(Vector3.Dot(f, u)) > 0.5f) return false;
            frame = new Frame(Vector3.Cross(u, f), f, u);
            return true;
        }

        public static Frame FromTransform(Transform t) => new Frame(t.right, t.forward, t.up);

        public static bool TryAxis(string axis, out Vector3 v)
        {
            v = default;
            if (string.IsNullOrEmpty(axis) || axis.Length != 2) return false;
            float sign;
            switch (axis[0]) { case '+': sign = 1; break; case '-': sign = -1; break; default: return false; }
            switch (char.ToLowerInvariant(axis[1]))
            {
                case 'x': v = Vector3.right * sign; return true;
                case 'y': v = Vector3.up * sign; return true;
                case 'z': v = Vector3.forward * sign; return true;
                default: return false;
            }
        }
    }
}
