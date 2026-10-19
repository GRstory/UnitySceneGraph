using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SceneGraph.Editor
{
    /// <summary>
    /// GameObject/Mesh/Material/Prefab 이름을 소문자 영어 토큰으로 쪼갠다.
    /// "SM_RedApple01" → sm(버림) red apple, "Red Apple-5095be0d..." → red apple.
    /// 복수형은 단수형도 같이 넣는다.
    /// </summary>
    public static class NameTokenizer
    {
        static readonly HashSet<string> Stop = new HashSet<string>
        {
            "sm", "sk", "skm", "mesh", "geo", "geometry", "prefab", "mat", "mtl", "material", "instance", "clone",
            "lod", "obj", "default", "node", "root", "model", "new", "gameobject", "object", "pivot", "the", "of",
        };

        public static List<string> Tokenize(string name)
        {
            var result = new List<string>();
            AddTokens(name, result);
            return result;
        }

        public static void AddTokens(string name, ICollection<string> into)
        {
            if (string.IsNullOrEmpty(name)) return;
            foreach (var raw in Split(name))
            {
                var t = raw.ToLowerInvariant();
                if (t.Length < 2 || Stop.Contains(t) || IsNumber(t) || IsHash(t)) continue;
                AddUnique(into, t);
                var singular = Singular(t);
                if (singular != t) AddUnique(into, singular);
            }
        }

        public static string Normalize(string noun)
        {
            if (string.IsNullOrEmpty(noun)) return noun;
            return noun.Trim().ToLowerInvariant();
        }

        public static string Singular(string t)
        {
            if (t.Length <= 3) return t;
            if (t.EndsWith("ies")) return t.Substring(0, t.Length - 3) + "y";
            if (t.EndsWith("ches") || t.EndsWith("shes") || t.EndsWith("xes") || t.EndsWith("sses")) return t.Substring(0, t.Length - 2);
            if (t.EndsWith("s") && !t.EndsWith("ss") && !t.EndsWith("us")) return t.Substring(0, t.Length - 1);
            return t;
        }

        /// <summary>
        /// 그룹: 루트 이름 + 루트가 프리팹 인스턴스 루트면 에셋 이름.
        /// 파츠: 자기 이름, 이 렌더러 하나만 감싼 조상 이름(Apple/Body의 Apple처럼 노드가 안 되는 이름 보존. 소속 그룹 직전까지),
        /// 메시/머티리얼 이름, 그 오브젝트들이 프리팹 인스턴스 루트면 에셋 이름. SemanticTag.overrideNouns가 있으면 그것만 쓴다.
        /// </summary>
        public static List<string> TokensForNode(IndexNode node, RendererSubtreeMap subtree)
        {
            var tokens = new List<string>();
            var tag = node.Root.GetComponent<SemanticTag>();
            if (tag != null && tag.OverridesNameTokens != null && tag.OverridesNameTokens.Length > 0)
            {
                foreach (var n in tag.OverridesNameTokens) AddTokens(n, tokens);
                return tokens;
            }

            if (node.Kind != IndexNodeKind.Part)
            {
                AddObjectName(node.Root, tokens);
                return tokens;
            }

            AddObjectName(node.Root, tokens);
            var stop = node.Parent?.Root.transform;
            for (var t = node.Root.transform == stop ? null : node.Root.transform.parent; t != null && t != stop; t = t.parent)
            {
                if (subtree[t].Count > 1) break;
                AddObjectName(t.gameObject, tokens);
            }

            var r = node.Renderers[0];
            switch (r)
            {
                case MeshRenderer _:
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) AddTokens(mf.sharedMesh.name, tokens);
                    break;
                case SkinnedMeshRenderer smr:
                    if (smr.sharedMesh != null) AddTokens(smr.sharedMesh.name, tokens);
                    break;
                case SpriteRenderer sr:
                    if (sr.sprite != null) AddTokens(sr.sprite.name, tokens);
                    break;
            }
            foreach (var m in r.sharedMaterials)
                if (m != null) AddTokens(m.name, tokens);
            return tokens;
        }

        static void AddObjectName(GameObject go, List<string> tokens)
        {
            AddTokens(go.name, tokens);
            if (PrefabUtility.IsAnyPrefabInstanceRoot(go))
            {
                var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                if (!string.IsNullOrEmpty(path)) AddTokens(System.IO.Path.GetFileNameWithoutExtension(path), tokens);
            }
        }

        static void AddUnique(ICollection<string> into, string t)
        {
            if (!into.Contains(t)) into.Add(t);
        }

        static IEnumerable<string> Split(string name)
        {
            var sb = new StringBuilder();
            char prev = '\0';
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (!char.IsLetterOrDigit(c))
                {
                    if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                    prev = '\0';
                    continue;
                }
                var boundary = sb.Length > 0 && (
                    (char.IsLower(prev) && char.IsUpper(c)) ||
                    (char.IsLetter(prev) && char.IsDigit(c)) ||
                    (char.IsDigit(prev) && char.IsLetter(c)) ||
                    // "HTTPServer" → HTTP Server
                    (char.IsUpper(prev) && char.IsUpper(c) && i + 1 < name.Length && char.IsLower(name[i + 1])));
                // 해시처럼 숫자와 문자가 섞인 긴 덩어리는 쪼개지 않고 통째로 버리기 위해 경계를 무시한다
                if (boundary && (char.IsDigit(prev) || char.IsDigit(c)) && LooksLikeHashRun(name, i)) boundary = false;
                if (boundary) { yield return sb.ToString(); sb.Clear(); }
                sb.Append(c);
                prev = c;
            }
            if (sb.Length > 0) yield return sb.ToString();
        }

        static bool LooksLikeHashRun(string name, int index)
        {
            var start = index;
            while (start > 0 && char.IsLetterOrDigit(name[start - 1])) start--;
            var end = index;
            while (end < name.Length && char.IsLetterOrDigit(name[end])) end++;
            return IsHash(name.Substring(start, end - start).ToLowerInvariant());
        }

        static bool IsNumber(string t)
        {
            foreach (var c in t)
                if (!char.IsDigit(c)) return false;
            return true;
        }

        static bool IsHash(string t)
        {
            if (t.Length < 8) return false;
            var digit = false;
            foreach (var c in t)
            {
                if (char.IsDigit(c)) digit = true;
                else if (c < 'a' || c > 'f') return false;
            }
            return digit;
        }
    }
}
