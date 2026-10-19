using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SceneGraph.Editor
{
    public sealed class RelationEvaluator
    {
        readonly RelationOptions options;

        public RelationEvaluator(RelationOptions options) => this.options = options;

        public bool RelationHolds(RelationType type, IndexNode target, IndexNode obj, Frame? frame)
        {
            if (target == obj) return false;

            if (type == RelationType.Contains)
            {
                // 계층 조건 먼저. 조상 제외 없음.
                if (target.IsAncestorOf(obj)) return true;
                return SpatialRelations.Holds(type, target.Bounds, obj.Bounds, Frame.World, options);
            }

            if (target.IsAncestorOf(obj)) return false;

            if (type == RelationType.On || type == RelationType.Above || type == RelationType.Below)
            {
                // 받침면은 object 그룹 상자가 아니라 파츠 단위. 옆 형제가 그룹 윗면을 올려도 깨지지 않는다.
                var a = target.Bounds;
                foreach (var support in SupportBounds(target, obj))
                    if (SpatialRelations.Holds(type, a, support, Frame.World, options)) return true;
                return false;
            }

            if (SpatialRelations.IsAxisDependent(type) && frame == null) return false;
            if (!TryRelationBounds(target, obj, out var objBounds)) return false;
            return SpatialRelations.Holds(type, target.Bounds, objBounds, frame ?? Frame.World, options);
        }

        /// <summary>object 서브트리 파츠 상자들(target 서브트리 파츠 제외). object가 파츠면 자기 하나, Terrain이면 지형 상자.</summary>
        static IEnumerable<Bounds> SupportBounds(IndexNode target, IndexNode obj)
        {
            if (obj.Kind == IndexNodeKind.Terrain)
            {
                yield return obj.Bounds;
                yield break;
            }
            var exclude = new HashSet<Renderer>(target.Renderers);
            foreach (var p in obj.SelfAndDescendants())
            {
                if (p.Kind != IndexNodeKind.Part || exclude.Contains(p.Renderers[0])) continue;
                if (BoundsUtil.TryWorldBounds(p.Renderers[0], out var b)) yield return b;
            }
        }

        /// <summary>
        /// 조상 제외 bounds. target이 obj 서브트리 안에 있으면 obj bounds를 target 서브트리만 뺀 채로 다시 계산한다.
        /// 다른 자식은 그대로 둔다. 남는 렌더러가 없으면 false.
        /// </summary>
        public static bool TryRelationBounds(IndexNode target, IndexNode obj, out Bounds bounds)
        {
            var exclude = obj.Renderers.Intersect(target.Renderers).Any() ? new HashSet<Renderer>(target.Renderers) : null;
            if (obj.Kind == IndexNodeKind.Terrain)
            {
                bounds = obj.Bounds;
                return true;
            }
            if (!BoundsUtil.HasAny(obj.Renderers, exclude))
            {
                bounds = default;
                return false;
            }
            bounds = BoundsUtil.Union(obj.Renderers, exclude);
            return true;
        }
    }
}
