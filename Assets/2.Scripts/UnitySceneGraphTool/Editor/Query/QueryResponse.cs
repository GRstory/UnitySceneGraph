using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SceneGraph.Editor
{
    public sealed class MatchedRelation
    {
        public RelationType Type;
        public IndexNode Object;
    }

    public sealed class QueryResult
    {
        public IndexNode Node;
        public float Score;
        public float NounScore, ColorScore, RelationScore;
        public List<string> MatchedNouns = new List<string>();
        public List<string> MatchedColors = new List<string>();
        public List<MatchedRelation> MatchedRelations = new List<MatchedRelation>();
    }

    public sealed class QueryResponse
    {
        public List<QueryResult> Results = new List<QueryResult>();
        public int Total;
        public int PendingLabels;

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"results\": [");
            for (var i = 0; i < Results.Count; i++)
            {
                var r = Results[i];
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append("    {\n");
                sb.Append("      \"globalObjectId\": ").Append(MiniJson.Quote(r.Node.GlobalObjectId)).Append(",\n");
                sb.Append("      \"path\": ").Append(MiniJson.Quote(r.Node.HierarchyPath)).Append(",\n");
                sb.Append("      \"score\": ").Append(r.Score.ToString("0.###", CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("      \"matched\": {\n");
                sb.Append("        \"nouns\": [").Append(string.Join(", ", r.MatchedNouns.Select(MiniJson.Quote))).Append("],\n");
                sb.Append("        \"colors\": [").Append(string.Join(", ", r.MatchedColors.Select(MiniJson.Quote))).Append("],\n");
                sb.Append("        \"relations\": [")
                    .Append(string.Join(", ", r.MatchedRelations.Select(m =>
                        $"{{ \"type\": {MiniJson.Quote(SpatialRelations.Name(m.Type))}, \"path\": {MiniJson.Quote(m.Object.HierarchyPath)} }}")))
                    .Append("]\n");
                sb.Append("      }\n    }");
            }
            sb.Append(Results.Count > 0 ? "\n  ],\n" : "],\n");
            sb.Append($"  \"indexStatus\": {{ \"total\": {Total}, \"pendingLabels\": {PendingLabels} }}\n}}");
            return sb.ToString();
        }
    }
}
