using System;
using System.Collections.Generic;
using System.Linq;

namespace SceneGraph.Editor
{
    /// <summary>target과 relation의 object가 같은 형식. relations를 넣으면 재귀한다.</summary>
    public sealed class NodeQuery
    {
        public List<string> Nouns = new List<string>();
        public List<string> Colors = new List<string>();
        public string NamePattern;
        public List<RelationQuery> Relations = new List<RelationQuery>();
    }

    public sealed class RelationQuery
    {
        public RelationType Type;
        public NodeQuery Object;
    }

    public enum FrameType { World, SceneCamera, Object }

    public sealed class FrameSpec
    {
        public FrameType Type = FrameType.World;
        public string Forward = "+z";
        public string Up = "+y";
        public NodeQuery Object;
    }

    /// <summary>
    /// 구조화 쿼리. PLAN.md 3.5. 최상위 relations는 target에 붙는다.
    /// "target" 키가 없으면 최상위 객체를 target으로 읽는다(PLAN.md 5절 M1 검증 쿼리 형식).
    /// </summary>
    public sealed class SceneQuery
    {
        public NodeQuery Target = new NodeQuery();
        /// <summary>null이면 설정 기본값(world).</summary>
        public FrameSpec Frame;
        public int Limit;

        public static SceneQuery Parse(string json)
        {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root))
                throw new FormatException("Query must be a JSON object");

            var q = new SceneQuery();
            if (root.TryGetValue("target", out var target))
            {
                q.Target = ParseNode(target as Dictionary<string, object> ?? throw new FormatException("target must be an object"));
                if (root.TryGetValue("relations", out var rels)) q.Target.Relations.AddRange(ParseRelations(rels));
            }
            else
            {
                q.Target = ParseNode(root);
            }

            if (root.TryGetValue("frame", out var frame) && frame != null) q.Frame = ParseFrame(frame as Dictionary<string, object>);
            if (root.TryGetValue("limit", out var limit) && limit is double d) q.Limit = (int)d;
            return q;
        }

        static NodeQuery ParseNode(Dictionary<string, object> o)
        {
            var n = new NodeQuery();
            if (o.TryGetValue("nouns", out var nouns)) n.Nouns = Strings(nouns).Select(NameTokenizer.Normalize).ToList();
            if (o.TryGetValue("colors", out var colors)) n.Colors = Strings(colors).Select(NameTokenizer.Normalize).ToList();
            if (o.TryGetValue("namePattern", out var pattern)) n.NamePattern = pattern as string;
            if (o.TryGetValue("relations", out var rels)) n.Relations.AddRange(ParseRelations(rels));
            return n;
        }

        static IEnumerable<RelationQuery> ParseRelations(object rels)
        {
            if (rels == null) yield break;
            if (!(rels is List<object> list)) throw new FormatException("relations must be an array");
            foreach (var item in list)
            {
                if (!(item is Dictionary<string, object> r)) throw new FormatException("relation must be an object");
                var typeName = r.TryGetValue("type", out var t) ? t as string : null;
                if (!SpatialRelations.TryParse(typeName, out var type)) throw new FormatException($"Unknown relation type '{typeName}'");
                if (!r.TryGetValue("object", out var obj) || !(obj is Dictionary<string, object> objDict))
                    throw new FormatException($"relation '{typeName}' needs an object");
                yield return new RelationQuery { Type = type, Object = ParseNode(objDict) };
            }
        }

        static FrameSpec ParseFrame(Dictionary<string, object> o)
        {
            if (o == null) throw new FormatException("frame must be an object");
            var f = new FrameSpec();
            var type = o.TryGetValue("type", out var t) ? (t as string ?? "world") : "world";
            switch (type)
            {
                case "world":
                    f.Type = FrameType.World;
                    if (o.TryGetValue("forward", out var fw) && fw is string fws) f.Forward = fws;
                    if (o.TryGetValue("up", out var up) && up is string ups) f.Up = ups;
                    break;
                case "sceneCamera":
                    f.Type = FrameType.SceneCamera;
                    break;
                case "object":
                    f.Type = FrameType.Object;
                    if (!o.TryGetValue("object", out var obj) || !(obj is Dictionary<string, object> objDict))
                        throw new FormatException("object frame needs an object");
                    f.Object = ParseNode(objDict);
                    break;
                default:
                    throw new FormatException($"Unknown frame type '{type}'");
            }
            return f;
        }

        static IEnumerable<string> Strings(object o)
        {
            if (o == null) return Enumerable.Empty<string>();
            if (o is string s) return new[] { s };
            if (o is List<object> list) return list.OfType<string>();
            throw new FormatException("Expected a string array");
        }
    }
}
