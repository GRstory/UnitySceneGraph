using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SceneGraph.Editor
{
    public sealed class SceneIndex
    {
        public readonly List<IndexNode> Roots = new List<IndexNode>();
        public readonly List<IndexNode> All = new List<IndexNode>();
        public readonly Dictionary<Renderer, IndexNode> PartByRenderer = new Dictionary<Renderer, IndexNode>();
        public readonly Dictionary<GameObject, IndexNode> GroupByRoot = new Dictionary<GameObject, IndexNode>();

        public IEnumerable<IndexNode> Groups => All.Where(n => n.Kind == IndexNodeKind.Group);
        public IEnumerable<IndexNode> Parts => All.Where(n => n.Kind == IndexNodeKind.Part);
    }

    /// <summary>
    /// 열린 씬의 렌더러를 훑어 파츠/그룹 트리를 만든다. 저장하지 않고 검색할 때마다 다시 만든다.
    /// </summary>
    public static class SceneIndexer
    {
        public static SceneIndex BuildLoadedScenes() => BuildLoadedScenes(SceneGraphSettings.instance.grouping);

        public static SceneIndex BuildLoadedScenes(GroupingOptions options)
        {
            var scenes = new List<Scene>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded) scenes.Add(s);
            }
            return Build(scenes, options);
        }

        public static SceneIndex Build(IEnumerable<Scene> scenes, GroupingOptions options) =>
            Build(scenes.SelectMany(s => s.GetRootGameObjects()), options);

        /// <summary>주어진 씬 루트 오브젝트들만 훑는다.</summary>
        public static SceneIndex Build(IEnumerable<GameObject> sceneRoots, GroupingOptions options)
        {
            options ??= new GroupingOptions();
            var roots = sceneRoots.Where(r => r != null).ToList();

            var lodDropped = new HashSet<Renderer>();
            foreach (var lodGroup in roots.SelectMany(r => r.GetComponentsInChildren<LODGroup>(true)))
            {
                var lods = lodGroup.GetLODs();
                if (lods.Length == 0) continue;
                var lod0 = new HashSet<Renderer>(lods[0].renderers.Where(r => r != null));
                for (var i = 1; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers)
                        if (r != null && !lod0.Contains(r)) lodDropped.Add(r);
            }

            var candidates = roots
                .SelectMany(r => r.GetComponentsInChildren<Renderer>(true))
                .Where(r => !lodDropped.Contains(r) && IsCandidate(r, options))
                .ToList();

            var subtree = new RendererSubtreeMap(candidates);
            var groupRoots = GroupingRules.Compute(candidates, subtree, options);
            var index = new SceneIndex();

            // 그룹 노드
            var groupByTransform = new Dictionary<Transform, IndexNode>();
            foreach (var pair in groupRoots)
            {
                var node = new IndexNode
                {
                    Kind = IndexNodeKind.Group,
                    Root = pair.Key.gameObject,
                    Signals = pair.Value,
                    Renderers = subtree[pair.Key].ToArray(),
                };
                groupByTransform[pair.Key] = node;
                index.GroupByRoot[node.Root] = node;
            }

            foreach (var pair in groupByTransform)
                Attach(index, pair.Value, NearestGroup(pair.Key.parent, groupByTransform));

            // 파츠 노드. 렌더러 오브젝트 자신이 그룹 루트면 그 그룹 소속.
            foreach (var r in candidates)
            {
                var node = new IndexNode { Kind = IndexNodeKind.Part, Root = r.gameObject, Renderers = new[] { r } };
                index.PartByRenderer[r] = node;
                Attach(index, node, NearestGroup(r.transform, groupByTransform));
            }

            foreach (var terrain in roots.SelectMany(r => r.GetComponentsInChildren<Terrain>(true)))
            {
                if (!IsCandidateObject(terrain.gameObject, options)) continue;
                var node = new IndexNode { Kind = IndexNodeKind.Terrain, Root = terrain.gameObject, Terrain = terrain };
                Attach(index, node, NearestGroup(terrain.transform.parent, groupByTransform));
            }

            foreach (var node in index.All)
            {
                node.HierarchyPath = PathOf(node.Root.transform);
                node.GlobalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(node.Root).ToString();
                node.NameTokens = NameTokenizer.TokensForNode(node, subtree).ToArray();
            }

            SortChildren(index.Roots);
            return index;
        }

        static void Attach(SceneIndex index, IndexNode node, IndexNode parent)
        {
            node.Parent = parent;
            if (parent != null) parent.Children.Add(node);
            else index.Roots.Add(node);
            index.All.Add(node);
        }

        static IndexNode NearestGroup(Transform t, Dictionary<Transform, IndexNode> groups)
        {
            for (; t != null; t = t.parent)
                if (groups.TryGetValue(t, out var g)) return g;
            return null;
        }

        static void SortChildren(List<IndexNode> nodes)
        {
            nodes.Sort((a, b) =>
            {
                var k = a.Kind.CompareTo(b.Kind);
                return k != 0 ? -k : string.CompareOrdinal(a.HierarchyPath, b.HierarchyPath);
            });
            foreach (var n in nodes) SortChildren(n.Children);
        }

        public static bool IsCandidate(Renderer r, GroupingOptions options)
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer || r is SpriteRenderer)) return false;
            if (!options.includeInactive && !r.enabled) return false;
            return IsCandidateObject(r.gameObject, options);
        }

        static bool IsCandidateObject(GameObject go, GroupingOptions options)
        {
            if (!options.includeInactive && !go.activeInHierarchy) return false;
            for (var t = go.transform; t != null; t = t.parent)
            {
                if (t.gameObject.hideFlags != HideFlags.None) return false;
                if (t.CompareTag("EditorOnly")) return false;
                var tag = t.GetComponent<SemanticTag>();
                if (tag != null && tag.ExcludeFromSearch) return false;
            }
            return true;
        }

        public static string PathOf(Transform t)
        {
            var names = new List<string>();
            for (; t != null; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
