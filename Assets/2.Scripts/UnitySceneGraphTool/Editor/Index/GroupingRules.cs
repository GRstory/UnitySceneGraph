using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SceneGraph.Editor
{
    /// <summary>
    /// 후보 렌더러 목록에서 그룹 루트를 정한다. PLAN.md 3.1 "그룹 만드는 규칙".
    /// 1. SemanticTag.isGroupRoot (그 아래 신호 무시, 위로는 규칙 2 계속)
    /// 2. 신호 조상 전부 (LODGroup, Regular/Variant 프리팹 인스턴스 루트, Rigidbody, Animator)
    /// 3. 가장 가까운 신호 그룹 바로 아래까지 휴리스틱 올라가기
    /// 4. 아무것도 없으면 파츠 자신
    /// </summary>
    public sealed class GroupingRules
    {
        readonly GroupingOptions options;
        readonly RendererSubtreeMap subtree;
        readonly Dictionary<Transform, Bounds?> subtreeBounds = new Dictionary<Transform, Bounds?>();
        readonly Dictionary<Renderer, Transform> nearestSignal = new Dictionary<Renderer, Transform>();
        readonly Dictionary<Transform, int> ownCount = new Dictionary<Transform, int>();

        /// <summary>그룹 루트 트랜스폼과 그 이유.</summary>
        public readonly Dictionary<Transform, GroupSignal> GroupRoots = new Dictionary<Transform, GroupSignal>();

        GroupingRules(GroupingOptions options, RendererSubtreeMap subtree)
        {
            this.options = options;
            this.subtree = subtree;
        }

        public static Dictionary<Transform, GroupSignal> Compute(IReadOnlyList<Renderer> candidates, RendererSubtreeMap subtree, GroupingOptions options)
        {
            var rules = new GroupingRules(options ?? new GroupingOptions(), subtree);
            rules.Run(candidates);
            return rules.GroupRoots;
        }

        void Run(IReadOnlyList<Renderer> candidates)
        {
            MarkSignalGroups();
            MarkHeuristicGroups(candidates);
        }

        void MarkSignalGroups()
        {
            // 규칙 1, 2: 신호 그룹
            foreach (var t in subtree.Transforms)
            {
                var s = EffectiveSignal(t);
                if (s != GroupSignal.None) GroupRoots[t] = s;
            }
        }

        void MarkHeuristicGroups(IReadOnlyList<Renderer> candidates)
        {
            foreach (var r in candidates)
            {
                var g = NearestSignal(r.transform);
                nearestSignal[r] = g;
                if (g != null) ownCount[g] = ownCount.TryGetValue(g, out var c) ? c + 1 : 1;
            }

            // 규칙 3: 휴리스틱. 렌더러마다 가장 가까운 신호 그룹 바로 아래까지만 올라간다.
            var heuristic = new HashSet<Transform>();
            foreach (var r in candidates)
            {
                var start = r.transform;
                var g0 = nearestSignal[r];
                if (start == g0) continue;

                var cur = start;
                while (true)
                {
                    var parent = cur.parent;
                    if (parent == null || parent == g0 || !PassesHeuristic(parent)) break;
                    cur = parent;
                }

                if (cur == start || heuristic.Contains(cur)) continue;
                if (IsRedundantHeuristicGroup(cur, g0)) continue;
                heuristic.Add(cur);
            }

            foreach (var h in heuristic)
                GroupRoots[h] = GroupRoots.TryGetValue(h, out var s) ? s | GroupSignal.Heuristic : GroupSignal.Heuristic;
        }

        bool IsRedundantHeuristicGroup(Transform stop, Transform signal)
        {
            if (subtree[stop].Count <= 1) return true; // 파츠가 하나뿐
            if (signal != null && SameAsOwnSet(stop, signal)) return true; // 신호 그룹과 같은 렌더러 집합 (중간 홀더)
            return false;
        }

        Transform NearestSignal(Transform t)
        {
            for (; t != null; t = t.parent)
                if (GroupRoots.ContainsKey(t)) return t;
            return null;
        }

        bool SameAsOwnSet(Transform stop, Transform signal)
        {
            var set = subtree[stop];
            if (!ownCount.TryGetValue(signal, out var own) || own != set.Count) return false;
            foreach (var r in set)
                if (nearestSignal[r] != signal) return false;
            return true;
        }

        bool PassesHeuristic(Transform parent)
        {
            if (subtree[parent].Count > options.maxParts) return false;
            if (Diagonal(parent) > options.maxDiagonal) return false;
            if (options.IsStopName(parent.name)) return false;
            var tag = parent.GetComponent<SemanticTag>();
            if (tag != null && tag.SplitChildren) return false;
            return true;
        }

        float Diagonal(Transform t)
        {
            if (!subtreeBounds.TryGetValue(t, out var b))
            {
                var list = subtree[t];
                subtreeBounds[t] = b = BoundsUtil.HasAny(list, null) ? BoundsUtil.Union(list) : (Bounds?)null;
            }
            return b?.size.magnitude ?? 0f;
        }

        GroupSignal EffectiveSignal(Transform t)
        {
            var tag = t.GetComponent<SemanticTag>();
            if (tag != null && tag.GroupRoot) return GroupSignal.Manual | RawSignal(t);
            if (tag != null && tag.SplitChildren) return GroupSignal.None;
            var s = RawSignal(t);
            if (s == GroupSignal.None) return s;
            for (var p = t.parent; p != null; p = p.parent)
            {
                var pt = p.GetComponent<SemanticTag>();
                if (pt != null && pt.GroupRoot) return GroupSignal.None; // 수동 그룹 아래 신호는 무시
            }
            return s;
        }

        GroupSignal RawSignal(Transform t)
        {
            var s = GroupSignal.None;
            if (t.GetComponent<LODGroup>() != null) s |= GroupSignal.LodGroup;
            if (t.GetComponent<Rigidbody>() != null) s |= GroupSignal.Rigidbody;
            if (t.GetComponent<Animator>() != null) s |= GroupSignal.Animator;
            // 과대 그룹 분할: 서브트리 렌더러가 maxParts를 넘는 프리팹은 신호가 아닌 것으로 친다
            if (IsSignalPrefabRoot(t.gameObject) && subtree[t].Count <= options.maxParts) s |= GroupSignal.Prefab;
            return s;
        }

        /// <summary>Regular 또는 Variant 프리팹 인스턴스 루트(중첩 포함). Model은 신호가 아니다.</summary>
        public static bool IsSignalPrefabRoot(GameObject go)
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) return false;
            var type = InstanceAssetType(go);
            return type == PrefabAssetType.Regular || type == PrefabAssetType.Variant;
        }

        /// <summary>
        /// 이 인스턴스 루트가 직접 가리키는 에셋의 타입. 중첩 인스턴스에 GetPrefabAssetType(go)를 바로 쓰면
        /// 바깥 프리팹 쪽 타입이 나올 수 있어서 가장 가까운 인스턴스 루트의 에셋을 로드해 본다.
        /// </summary>
        public static PrefabAssetType InstanceAssetType(GameObject go)
        {
            var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            var asset = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path) as GameObject;
            return asset != null ? PrefabUtility.GetPrefabAssetType(asset) : PrefabUtility.GetPrefabAssetType(go);
        }
    }
}
