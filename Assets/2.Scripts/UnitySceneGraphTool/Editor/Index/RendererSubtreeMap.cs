using System.Collections.Generic;
using UnityEngine;

namespace SceneGraph.Editor
{
    /// <summary>트랜스폼 서브트리에 든 후보 렌더러.</summary>
    public sealed class RendererSubtreeMap
    {
        readonly Dictionary<Transform, List<Renderer>> subtree = new Dictionary<Transform, List<Renderer>>();

        public RendererSubtreeMap(IEnumerable<Renderer> candidates)
        {
            foreach (var r in candidates)
                for (var t = r.transform; t != null; t = t.parent)
                {
                    if (!subtree.TryGetValue(t, out var list)) subtree[t] = list = new List<Renderer>();
                    list.Add(r);
                }
        }

        public List<Renderer> this[Transform t] => subtree[t];

        public bool TryGet(Transform t, out List<Renderer> renderers) => subtree.TryGetValue(t, out renderers);

        public IEnumerable<Transform> Transforms => subtree.Keys;
    }
}
