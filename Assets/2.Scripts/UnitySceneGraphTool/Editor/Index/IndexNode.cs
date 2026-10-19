using System;
using System.Collections.Generic;
using UnityEngine;

namespace SceneGraph.Editor
{
    public enum IndexNodeKind { Part, Group, Terrain }

    /// <summary>그룹이 왜 생겼는지. 같은 오브젝트에 신호가 여럿이면 플래그가 겹친다.</summary>
    [Flags]
    public enum GroupSignal
    {
        None = 0,
        Manual = 1,
        LodGroup = 2,
        Prefab = 4,
        Rigidbody = 8,
        Animator = 16,
        Heuristic = 32,
    }

    /// <summary>
    /// 인덱스 트리 노드. 위치와 bounds는 저장하지 않고 접근할 때 라이브로 읽는다. PLAN.md 3.1.
    /// </summary>
    public sealed class IndexNode
    {
        public IndexNodeKind Kind;
        public GameObject Root;
        /// <summary>파츠는 자기 렌더러 하나, 그룹은 서브트리 렌더러 전체(소속 파츠 + 자식 그룹).</summary>
        public Renderer[] Renderers = Array.Empty<Renderer>();
        public Terrain Terrain;
        public GroupSignal Signals;
        public string HierarchyPath;
        public string GlobalObjectId;
        public string[] NameTokens = Array.Empty<string>();
        public IndexNode Parent;
        public readonly List<IndexNode> Children = new List<IndexNode>();
        /// <summary>M2에서 채운다.</summary>
        public Hash128 Fingerprint;

        public bool Inactive => Root != null && !Root.activeInHierarchy;
        public Bounds Bounds => Kind == IndexNodeKind.Terrain ? BoundsUtil.TerrainBounds(Terrain) : BoundsUtil.Union(Renderers);

        public bool IsAncestorOf(IndexNode node)
        {
            for (var p = node?.Parent; p != null; p = p.Parent)
                if (p == this) return true;
            return false;
        }

        public IEnumerable<IndexNode> SelfAndDescendants()
        {
            yield return this;
            foreach (var child in Children)
                foreach (var n in child.SelfAndDescendants())
                    yield return n;
        }

        public override string ToString() => $"{Kind}:{HierarchyPath}";
    }

    public static class BoundsUtil
    {
        /// <summary>
        /// Renderer.bounds의 월드 AABB. 비활성 렌더러는 bounds가 비어 나오므로 메시 로컬 bounds를 변환해 쓴다.
        /// </summary>
        public static bool TryWorldBounds(Renderer r, out Bounds bounds)
        {
            bounds = default;
            if (r == null) return false;
            if (r.enabled && r.gameObject.activeInHierarchy)
            {
                bounds = r.bounds;
                return true;
            }

            Bounds local;
            switch (r)
            {
                case MeshRenderer _:
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) return false;
                    local = mf.sharedMesh.bounds;
                    break;
                case SkinnedMeshRenderer smr:
                    if (smr.sharedMesh == null) return false;
                    local = smr.sharedMesh.bounds;
                    break;
                case SpriteRenderer sr:
                    if (sr.sprite == null) return false;
                    local = sr.sprite.bounds;
                    break;
                default:
                    return false;
            }
            bounds = Transform(local, r.transform.localToWorldMatrix);
            return true;
        }

        public static Bounds Transform(Bounds local, Matrix4x4 m)
        {
            var center = m.MultiplyPoint3x4(local.center);
            var e = local.extents;
            var x = m.MultiplyVector(new Vector3(e.x, 0, 0));
            var y = m.MultiplyVector(new Vector3(0, e.y, 0));
            var z = m.MultiplyVector(new Vector3(0, 0, e.z));
            var extents = new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(center, extents * 2f);
        }

        public static Bounds Union(IEnumerable<Renderer> renderers) => Union(renderers, null);

        /// <summary>exclude에 든 렌더러를 뺀 합집합. 남는 게 없으면 size 0, center 0.</summary>
        public static Bounds Union(IEnumerable<Renderer> renderers, ICollection<Renderer> exclude)
        {
            var any = false;
            var result = default(Bounds);
            foreach (var r in renderers)
            {
                if (exclude != null && exclude.Contains(r)) continue;
                if (!TryWorldBounds(r, out var b)) continue;
                if (!any) { result = b; any = true; }
                else result.Encapsulate(b);
            }
            return result;
        }

        public static bool HasAny(IEnumerable<Renderer> renderers, ICollection<Renderer> exclude)
        {
            foreach (var r in renderers)
                if ((exclude == null || !exclude.Contains(r)) && TryWorldBounds(r, out _)) return true;
            return false;
        }

        public static Bounds TerrainBounds(Terrain t)
        {
            if (t == null || t.terrainData == null) return default;
            var b = t.terrainData.bounds;
            b.center += t.GetPosition();
            return b;
        }
    }
}
