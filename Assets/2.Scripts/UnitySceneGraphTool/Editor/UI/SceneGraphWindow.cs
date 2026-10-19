using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SceneGraph.Editor
{
    /// <summary>인덱스 트리와 bounds 보기, 쿼리 JSON 입력 → 결과 선택/ping.</summary>
    public sealed class SceneGraphWindow : EditorWindow
    {
        const string DefaultQuery =
            "{\"target\":{\"nouns\":[\"apple\"],\"colors\":[\"red\"]},\"relations\":[{\"type\":\"on\",\"object\":{\"nouns\":[\"desk\"]}}]}";

        static readonly string[] Tabs = { "Index", "Query" };

        [SerializeField] int tab;
        [SerializeField] string queryJson = DefaultQuery;
        [SerializeField] bool showParts = true;

        SceneIndex index;
        readonly HashSet<IndexNode> expanded = new HashSet<IndexNode>();
        IndexNode selected;
        Texture2D thumbnail;
        IReadOnlyDictionary<string, float> thumbnailColors;
        Vector2 treeScroll, queryScroll;
        QueryResponse response;
        string responseJson;
        string error;

        [MenuItem("Window/Unity Scene Graph")]
        public static void Open() => GetWindow<SceneGraphWindow>("Scene Graph");

        void OnDisable() => ClearThumbnail();

        void OnGUI()
        {
            tab = GUILayout.Toolbar(tab, Tabs);
            if (tab == 0) IndexTab();
            else QueryTab();
        }

        void IndexTab()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild Index", GUILayout.Width(120))) Rebuild();
                showParts = GUILayout.Toggle(showParts, "Show parts", GUILayout.Width(90));
                if (GUILayout.Button("Expand all", GUILayout.Width(90)) && index != null)
                    foreach (var n in index.All) expanded.Add(n);
                if (GUILayout.Button("Collapse", GUILayout.Width(80))) expanded.Clear();
                if (index != null)
                    GUILayout.Label($"nodes {index.All.Count}  groups {index.Groups.Count()}  parts {index.Parts.Count()}");
            }
            if (index == null)
            {
                EditorGUILayout.HelpBox("Rebuild Index를 누르면 열린 씬을 훑는다.", MessageType.Info);
                return;
            }

            treeScroll = EditorGUILayout.BeginScrollView(treeScroll);
            foreach (var n in index.Roots) DrawNode(n, 0);
            EditorGUILayout.EndScrollView();

            if (selected != null) SelectedPanel();
        }

        void DrawNode(IndexNode node, int depth)
        {
            if (node.Root == null) return;
            if (!showParts && node.Kind == IndexNodeKind.Part) return;
            var hasChildren = node.Children.Any(c => showParts || c.Kind != IndexNodeKind.Part);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(depth * 14);
                if (hasChildren)
                {
                    var open = expanded.Contains(node);
                    var now = EditorGUILayout.Foldout(open, GUIContent.none, true);
                    if (now != open) { if (now) expanded.Add(node); else expanded.Remove(node); }
                }
                else GUILayout.Space(14);

                var style = node == selected ? EditorStyles.boldLabel : EditorStyles.label;
                if (GUILayout.Button(Label(node), style)) Select(node);
            }
            if (hasChildren && expanded.Contains(node))
                foreach (var c in node.Children) DrawNode(c, depth + 1);
        }

        static string Label(IndexNode n)
        {
            var b = n.Bounds;
            var kind = n.Kind == IndexNodeKind.Group ? $"[G {n.Signals}]" : n.Kind == IndexNodeKind.Part ? "[P]" : "[T]";
            var inactive = n.Inactive ? " (inactive)" : "";
            var parts = n.Kind == IndexNodeKind.Group ? $" r={n.Renderers.Length}" : "";
            return $"{kind} {n.Root.name}{inactive}{parts}  c={V(b.center)} s={V(b.size)}";
        }

        static string V(Vector3 v) =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", v.x, v.y, v.z);

        void Select(IndexNode node)
        {
            selected = node;
            ClearThumbnail();
            Selection.activeGameObject = node.Root;
            EditorGUIUtility.PingObject(node.Root);
        }

        void SelectedPanel()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(selected.HierarchyPath, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("tokens", string.Join(", ", selected.NameTokens));
            EditorGUILayout.LabelField("bounds", $"min {V(selected.Bounds.min)} max {V(selected.Bounds.max)}");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Thumbnail + colors", GUILayout.Width(150)) && selected.Renderers.Length > 0)
                {
                    ClearThumbnail();
                    thumbnail = ThumbnailRenderer.Render(selected, SceneGraphSettings.instance.thumbnailSize, Color.black);
                    thumbnailColors = ThumbnailColorProvider.ThumbnailColors(selected);
                }
                if (thumbnail != null) GUILayout.Label(thumbnail, GUILayout.Width(128), GUILayout.Height(128));
                if (thumbnailColors != null)
                    GUILayout.Label(string.Join("\n", thumbnailColors.OrderByDescending(p => p.Value)
                        .Select(p => $"{p.Key} {p.Value:0.00}")));
            }
        }

        void ClearThumbnail()
        {
            if (thumbnail != null) DestroyImmediate(thumbnail);
            thumbnail = null;
            thumbnailColors = null;
        }

        void Rebuild()
        {
            index = SceneIndexer.BuildLoadedScenes();
            selected = null;
            ClearThumbnail();
            expanded.Clear();
            foreach (var n in index.Groups) expanded.Add(n);
        }

        void QueryTab()
        {
            EditorGUILayout.LabelField("Query JSON (PLAN.md 3.5)");
            queryJson = EditorGUILayout.TextArea(queryJson, GUILayout.MinHeight(80));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Run", GUILayout.Width(80))) RunQuery();
                if (response != null && response.Results.Count > 0 && GUILayout.Button("Select all", GUILayout.Width(90)))
                {
                    Selection.objects = response.Results.Select(r => (UnityEngine.Object)r.Node.Root).ToArray();
                    EditorGUIUtility.PingObject(response.Results[0].Node.Root);
                }
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (response == null) return;

            queryScroll = EditorGUILayout.BeginScrollView(queryScroll);
            EditorGUILayout.LabelField($"{response.Results.Count} results (index {response.Total} nodes)");
            foreach (var r in response.Results)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(r.Score.ToString("0.000", CultureInfo.InvariantCulture), GUILayout.Width(50));
                    if (GUILayout.Button($"{r.Node.Kind} {r.Node.HierarchyPath}", EditorStyles.linkLabel))
                    {
                        Selection.activeGameObject = r.Node.Root;
                        EditorGUIUtility.PingObject(r.Node.Root);
                    }
                }
                var rel = string.Join(", ", r.MatchedRelations.Select(m => $"{SpatialRelations.Name(m.Type)} {m.Object.HierarchyPath}"));
                EditorGUILayout.LabelField($"   noun {r.NounScore:0.00} color {r.ColorScore:0.00} rel {r.RelationScore:0.00}  {rel}",
                    EditorStyles.miniLabel);
            }
            EditorGUILayout.Space();
            EditorGUILayout.TextArea(responseJson);
            EditorGUILayout.EndScrollView();
        }

        void RunQuery()
        {
            error = null;
            response = null;
            try
            {
                response = QueryEngine.Run(queryJson);
                responseJson = response.ToJson();
            }
            catch (Exception e)
            {
                error = e.Message;
            }
        }
    }
}
