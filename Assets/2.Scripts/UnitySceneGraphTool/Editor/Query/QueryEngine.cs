using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

namespace SceneGraph.Editor
{
    /// <summary>
    /// 구조화 쿼리를 한 번에 평가한다. PLAN.md 4.2.
    /// total = nounScore × colorScore × relScore. nounScore는 M1에서 이름 토큰 정확 일치(1.0)만.
    /// </summary>
    public sealed class QueryEngine
    {
        public delegate IReadOnlyDictionary<string, float> ColorProvider(IndexNode node);

        readonly SceneIndex index;
        readonly RelationOptions options;
        readonly ColorProvider colorProvider;
        readonly RelationEvaluator relations;
        readonly Dictionary<IndexNode, IReadOnlyDictionary<string, float>> colorCache = new Dictionary<IndexNode, IReadOnlyDictionary<string, float>>();
        readonly Dictionary<NodeQuery, List<QueryResult>> evaluated = new Dictionary<NodeQuery, List<QueryResult>>();

        public QueryEngine(SceneIndex index, RelationOptions options = null, ColorProvider colorProvider = null)
        {
            this.index = index;
            this.options = options ?? new RelationOptions();
            this.colorProvider = colorProvider ?? ThumbnailColorProvider.ThumbnailColors;
            relations = new RelationEvaluator(this.options);
        }

        public static QueryResponse Run(string json) =>
            new QueryEngine(SceneIndexer.BuildLoadedScenes(), SceneGraphSettings.instance.relations).Run(SceneQuery.Parse(json));

        public QueryResponse Run(SceneQuery query)
        {
            evaluated.Clear();
            var frame = ResolveFrame(query.Frame);
            var results = Evaluate(query.Target, frame);
            var kept = RemoveNestedDuplicates(results, query.Target.Nouns.Count > 0);
            var limit = query.Limit > 0 ? query.Limit : options.defaultLimit;
            return new QueryResponse
            {
                Results = kept.OrderByDescending(r => r.Score).ThenBy(r => r.Node.HierarchyPath, StringComparer.Ordinal).Take(limit).ToList(),
                Total = index.All.Count,
            };
        }

        /// <summary>null이면 축 의존 관계는 성립하지 않는다(object frame 대상 없음, 씬 뷰 없음, 잘못된 축).</summary>
        public Frame? ResolveFrame(FrameSpec spec)
        {
            if (spec == null)
                return Frame.TryWorld(options.defaultFrameForward, options.defaultFrameUp, out var def) ? def : (Frame?)null;
            switch (spec.Type)
            {
                case FrameType.World:
                    return Frame.TryWorld(spec.Forward, spec.Up, out var world) ? world : (Frame?)null;
                case FrameType.SceneCamera:
                    var view = SceneView.lastActiveSceneView;
                    return view != null && view.camera != null ? Frame.FromTransform(view.camera.transform) : (Frame?)null;
                case FrameType.Object:
                    var best = Evaluate(spec.Object, Frame.World).OrderByDescending(r => r.Score).FirstOrDefault();
                    return best != null ? Frame.FromTransform(best.Node.Root.transform) : (Frame?)null;
            }
            return null;
        }

        List<QueryResult> Evaluate(NodeQuery q, Frame? frame)
        {
            if (evaluated.TryGetValue(q, out var cached)) return cached;
            var results = new List<QueryResult>();
            evaluated[q] = results;

            var nouns = q.Nouns.Select(NameTokenizer.Normalize).Where(n => !string.IsNullOrEmpty(n)).ToList();
            var nounForms = nouns.Select(n => (noun: n, singular: NameTokenizer.Singular(n))).ToList();
            var regex = string.IsNullOrEmpty(q.NamePattern) ? null : new Regex(q.NamePattern, RegexOptions.IgnoreCase);

            foreach (var node in index.All)
            {
                if (regex != null && !regex.IsMatch(node.Root.name)) continue;

                var r = new QueryResult { Node = node };
                if (nounForms.Count == 0) r.NounScore = 1f;
                else
                {
                    foreach (var (noun, singular) in nounForms)
                        if (node.NameTokens.Contains(noun) || node.NameTokens.Contains(singular))
                            r.MatchedNouns.Add(noun);
                    r.NounScore = r.MatchedNouns.Count > 0 ? 1f : 0f;
                }
                if (r.NounScore < options.minNounScore) continue;

                if (q.Colors.Count == 0) r.ColorScore = 1f;
                else
                {
                    var colors = ColorsOf(node);
                    foreach (var c in q.Colors)
                        if (colors.TryGetValue(c, out var ratio) && ratio > 0f)
                        {
                            r.ColorScore += ratio;
                            r.MatchedColors.Add(c);
                        }
                }
                if (r.ColorScore <= 0f) continue;

                r.RelationScore = 1f;
                foreach (var rel in q.Relations)
                {
                    var best = 0f;
                    IndexNode bestObject = null;
                    foreach (var o in Evaluate(rel.Object, frame))
                    {
                        if (o.Score <= best || !relations.RelationHolds(rel.Type, node, o.Node, frame)) continue;
                        best = o.Score;
                        bestObject = o.Node;
                    }
                    r.RelationScore *= best;
                    if (bestObject == null) break;
                    r.MatchedRelations.Add(new MatchedRelation { Type = rel.Type, Object = bestObject });
                }
                if (r.RelationScore <= 0f) continue;

                r.Score = r.NounScore * r.ColorScore * r.RelationScore;
                results.Add(r);
            }
            return results;
        }

        IReadOnlyDictionary<string, float> ColorsOf(IndexNode node)
        {
            if (!colorCache.TryGetValue(node, out var colors))
                colorCache[node] = colors = colorProvider(node) ?? new Dictionary<string, float>();
            return colors;
        }

        public bool RelationHolds(RelationType type, IndexNode target, IndexNode obj, Frame? frame) =>
            relations.RelationHolds(type, target, obj, frame);

        /// <summary>부모와 자식이 같은 명사로 걸리면 부모만 남긴다. 명사 없는 쿼리는 조상-자손 쌍이면 부모만.</summary>
        static List<QueryResult> RemoveNestedDuplicates(List<QueryResult> results, bool byNoun)
        {
            var byNode = results.ToDictionary(r => r.Node);
            var kept = new List<QueryResult>();
            foreach (var r in results)
            {
                var drop = false;
                for (var p = r.Node.Parent; p != null && !drop; p = p.Parent)
                    if (byNode.TryGetValue(p, out var ancestor))
                        drop = !byNoun || ancestor.MatchedNouns.Intersect(r.MatchedNouns).Any();
                if (!drop) kept.Add(r);
            }
            return kept;
        }
    }
}
