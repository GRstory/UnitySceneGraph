using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UsdExport.Editor
{
    /// <summary>
    /// 씬 전체를 외부 의존성 없이 USDA(텍스트 USD)로 내보낸다.
    /// 계층 → Xform, Mesh/SkinnedMesh → Mesh(+GeomSubset), 머티리얼 → UsdPreviewSurface, Camera, Light.
    /// Unity(왼손 좌표계) → USD(오른손, Y-up)는 Z축 반전 + 와인딩 뒤집기로 변환한다.
    /// </summary>
    public static class UsdSceneExporter
    {
        [MenuItem("Tools/USD/Export Active Scene...")]
        static void ExportActiveSceneMenu()
        {
            var scene = SceneManager.GetActiveScene();
            var name = string.IsNullOrEmpty(scene.name) ? "Scene" : scene.name;
            var path = EditorUtility.SaveFilePanel("Export Scene to USD", "", name, "usda");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                Export(scene, path);
                Debug.Log($"[USD] exported {scene.name} → {path}");
                EditorUtility.RevealInFinder(path);
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        /// <summary>배치모드: Unity -batchmode -quit -projectPath . -executeMethod UsdExport.Editor.UsdSceneExporter.ExportFromCommandLine -usdScene Assets/x.unity -usdOut out.usda</summary>
        public static void ExportFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string key)
            {
                var i = System.Array.IndexOf(args, key);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            var scenePath = Arg("-usdScene");
            var outPath = Arg("-usdOut") ?? Path.ChangeExtension(Path.GetFileName(scenePath), ".usda");
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
            Export(scene, Path.GetFullPath(outPath));
        }

        /// <summary>텍스처는 path 옆 "&lt;파일명&gt;_textures" 폴더에 PNG/JPG로 함께 저장된다.</summary>
        public static void Export(Scene scene, string path) => new Writer(path).Run(scene);

        sealed class Writer
        {
            static readonly CultureInfo CI = CultureInfo.InvariantCulture;

            readonly string path, texDirName, texDir;
            readonly Dictionary<Material, string> materials = new Dictionary<Material, string>();
            readonly List<Material> materialOrder = new List<Material>();
            readonly HashSet<string> materialNames = new HashSet<string>();
            readonly Dictionary<Texture, string> textures = new Dictionary<Texture, string>();
            readonly HashSet<string> textureFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            TextWriter w;
            int depth;
            string materialsPath;

            public Writer(string path)
            {
                this.path = path;
                texDirName = Path.GetFileNameWithoutExtension(path) + "_textures";
                texDir = Path.Combine(Path.GetDirectoryName(path) ?? "", texDirName);
            }

            public void Run(Scene scene)
            {
                var rootName = Sanitize(string.IsNullOrEmpty(scene.name) ? "Scene" : scene.name);
                var rootPath = "/" + rootName;
                materialsPath = rootPath + "/Materials";
                var roots = scene.GetRootGameObjects();

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                using (w = new StreamWriter(path, false, new UTF8Encoding(false)))
                {
                    Line("#usda 1.0");
                    Line("(");
                    depth++;
                    Line($"defaultPrim = \"{rootName}\"");
                    Line($"doc = \"Exported from Unity scene {Esc(scene.path)}\"");
                    Line("metersPerUnit = 1");
                    Line("upAxis = \"Y\"");
                    depth--;
                    Line(")");
                    Line("");

                    Open("Xform", rootName);
                    var names = new HashSet<string> { "Materials" };
                    for (int i = 0; i < roots.Length; i++)
                    {
                        EditorUtility.DisplayProgressBar("Export USD", roots[i].name, (float)i / roots.Length);
                        WriteGameObject(roots[i], rootPath, names);
                    }
                    EditorUtility.DisplayProgressBar("Export USD", "Materials", 1f);
                    WriteMaterials();
                    Close();
                }
            }

            // ---------- 계층 ----------

            void WriteGameObject(GameObject go, string parentPath, HashSet<string> siblings)
            {
                var name = Unique(Sanitize(go.name), siblings);
                var primPath = parentPath + "/" + name;
                Open("Xform", name, name != go.name ? $"customData = {{ string unityName = \"{Esc(go.name)}\" }}" : null);
                if (!go.activeSelf) Line("token visibility = \"invisible\"");

                var t = go.transform;
                Vector3 p = t.localPosition, s = t.localScale;
                var q = t.localRotation;
                Line($"double3 xformOp:translate = ({F(p.x)}, {F(p.y)}, {F(-p.z)})");
                // Z 반전 S·R·S → 쿼터니언 (w, -x, -y, z). USD quat 리터럴은 실수부가 먼저.
                Line($"quatf xformOp:orient = ({F(q.w)}, {F(-q.x)}, {F(-q.y)}, {F(q.z)})");
                Line($"float3 xformOp:scale = ({F(s.x)}, {F(s.y)}, {F(s.z)})");
                Line("uniform token[] xformOpOrder = [\"xformOp:translate\", \"xformOp:orient\", \"xformOp:scale\"]");

                // 컴포넌트 prim이 먼저 이름을 차지하고, 같은 이름의 자식은 _1 접미사를 받는다.
                var names = new HashSet<string>();
                WriteComponents(go, names);
                foreach (Transform child in t) WriteGameObject(child.gameObject, primPath, names);
                Close();
            }

            void WriteComponents(GameObject go, HashSet<string> names)
            {
                if (go.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh && go.TryGetComponent<MeshRenderer>(out var mr))
                    WriteMesh(Unique("Mesh", names), mf.sharedMesh, mr.sharedMaterials, mr.enabled);

                if (go.TryGetComponent<SkinnedMeshRenderer>(out var smr) && smr.sharedMesh)
                {
                    // 현재 포즈를 렌더러 로컬 공간으로 굽는다(스케일은 Xform이 적용).
                    var baked = new Mesh();
                    try
                    {
                        smr.BakeMesh(baked);
                        WriteMesh(Unique("Mesh", names), baked, smr.sharedMaterials, smr.enabled);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(baked); }
                }

                if (go.TryGetComponent<Camera>(out var cam)) WriteCamera(Unique("Camera", names), cam);
                if (go.TryGetComponent<Light>(out var light)) WriteLight(Unique("Light", names), light);
            }

            // ---------- 메시 ----------

            void WriteMesh(string name, Mesh mesh, Material[] mats, bool visible)
            {
                Vector3[] verts;
                try { verts = mesh.vertices; }
                catch (Exception e)
                {
                    Debug.LogWarning($"[USD] mesh '{mesh.name}' skipped: {e.Message}");
                    return;
                }
                if (verts.Length == 0) return;

                var counts = new List<int>();
                var indices = new List<int>();
                var subsets = new List<(int sub, int start, int count)>();
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var topo = mesh.GetTopology(sub);
                    int n = topo == MeshTopology.Triangles ? 3 : topo == MeshTopology.Quads ? 4 : 0;
                    if (n == 0) continue;
                    var idx = mesh.GetIndices(sub);
                    int start = counts.Count;
                    for (int i = 0; i + n <= idx.Length; i += n)
                    {
                        counts.Add(n);
                        for (int k = n - 1; k >= 0; k--) indices.Add(idx[i + k]); // Z 반전에 맞춰 와인딩 역순
                    }
                    if (counts.Count > start) subsets.Add((sub, start, counts.Count - start));
                }
                if (counts.Count == 0) return;

                Material MatOf(int sub) => sub < mats.Length ? mats[sub] : null;
                bool single = true;
                foreach (var s in subsets) single &= MatOf(s.sub) == MatOf(subsets[0].sub);

                Open("Mesh", name, "prepend apiSchemas = [\"MaterialBindingAPI\"]");
                if (!visible) Line("token visibility = \"invisible\"");
                var b = mesh.bounds;
                Line($"float3[] extent = [({F(b.min.x)}, {F(b.min.y)}, {F(-b.max.z)}), ({F(b.max.x)}, {F(b.max.y)}, {F(-b.min.z)})]");
                WriteArray("int[] faceVertexCounts", counts, c => c.ToString(CI));
                WriteArray("int[] faceVertexIndices", indices, i => i.ToString(CI));
                WriteArray("point3f[] points", verts, Vec);

                var normals = mesh.normals;
                if (normals.Length == verts.Length)
                    WriteArray("normal3f[] normals", normals, Vec, "interpolation = \"vertex\"");
                var uvs = mesh.uv;
                if (uvs.Length == verts.Length)
                    WriteArray("texCoord2f[] primvars:st", uvs, v => $"({F(v.x)}, {F(v.y)})", "interpolation = \"vertex\"");
                var colors = mesh.colors;
                if (colors.Length == verts.Length)
                    WriteArray("color3f[] primvars:displayColor", colors, c => Col(c.linear), "interpolation = \"vertex\"");
                Line("uniform token subdivisionScheme = \"none\"");

                if (single)
                {
                    var m = MatOf(subsets[0].sub);
                    if (m) Line($"rel material:binding = <{MaterialPath(m)}>");
                }
                else
                {
                    Line("uniform token subsetFamily:materialBind:familyType = \"nonOverlapping\"");
                    foreach (var s in subsets)
                    {
                        Open("GeomSubset", "Sub" + s.sub, "prepend apiSchemas = [\"MaterialBindingAPI\"]");
                        Line("uniform token elementType = \"face\"");
                        Line("uniform token familyName = \"materialBind\"");
                        var faces = new int[s.count];
                        for (int i = 0; i < faces.Length; i++) faces[i] = s.start + i;
                        WriteArray("int[] indices", faces, i => i.ToString(CI));
                        var m = MatOf(s.sub);
                        if (m) Line($"rel material:binding = <{MaterialPath(m)}>");
                        Close();
                    }
                }
                Close();
            }

            // ---------- 카메라 / 라이트 ----------

            void WriteCamera(string name, Camera cam)
            {
                // USD 카메라는 -Z를 본다. Unity +Z 전방은 Z 반전 후 -Z가 되므로 그대로 맞는다.
                Open("Camera", name);
                float aspect = cam.aspect > 0 ? cam.aspect : 16f / 9f;
                float hAp, vAp;
                if (cam.orthographic)
                {
                    Line("token projection = \"orthographic\"");
                    vAp = cam.orthographicSize * 2f * 10f; // aperture 단위는 씬 단위의 1/10
                    hAp = vAp * aspect;
                }
                else
                {
                    Line("token projection = \"perspective\"");
                    float focal;
                    if (cam.usePhysicalProperties)
                    {
                        vAp = cam.sensorSize.y;
                        hAp = cam.sensorSize.x;
                        focal = cam.focalLength;
                    }
                    else
                    {
                        vAp = 24f;
                        hAp = vAp * aspect;
                        focal = 0.5f * vAp / Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f);
                    }
                    Line($"float focalLength = {F(focal)}");
                }
                Line($"float horizontalAperture = {F(hAp)}");
                Line($"float verticalAperture = {F(vAp)}");
                Line($"float2 clippingRange = ({F(cam.nearClipPlane)}, {F(cam.farClipPlane)})");
                Close();
            }

            void WriteLight(string name, Light light)
            {
                switch (light.type)
                {
                    case LightType.Directional:
                        Open("DistantLight", name);
                        break;
                    case LightType.Point:
                        Open("SphereLight", name);
                        Line("bool treatAsPoint = 1");
                        break;
                    case LightType.Spot:
                        Open("SphereLight", name, "prepend apiSchemas = [\"ShapingAPI\"]");
                        Line("bool treatAsPoint = 1");
                        Line($"float inputs:shaping:cone:angle = {F(light.spotAngle * 0.5f)}");
                        break;
                    case LightType.Rectangle:
                        Open("RectLight", name);
                        Line($"float inputs:width = {F(light.areaSize.x)}");
                        Line($"float inputs:height = {F(light.areaSize.y)}");
                        break;
                    case LightType.Disc:
                        Open("DiskLight", name);
                        Line($"float inputs:radius = {F(light.areaSize.x)}");
                        break;
                    default:
                        return;
                }
                if (!light.enabled) Line("token visibility = \"invisible\"");
                Line($"color3f inputs:color = {Col(light.color.linear)}");
                Line($"float inputs:intensity = {F(light.intensity)}");
                Close();
            }

            // ---------- 머티리얼 ----------

            string MaterialPath(Material mat)
            {
                if (materials.TryGetValue(mat, out var p)) return p;
                p = materialsPath + "/" + Unique(Sanitize(mat.name), materialNames);
                materials[mat] = p;
                materialOrder.Add(mat);
                return p;
            }

            void WriteMaterials()
            {
                if (materialOrder.Count == 0) return;
                Open("Scope", "Materials");
                foreach (var mat in materialOrder) WriteMaterial(mat, materials[mat]);
                Close();
            }

            /// <summary>URP Lit / Built-in Standard / glTFast 셰이더의 주요 프로퍼티를 UsdPreviewSurface로 근사한다.</summary>
            void WriteMaterial(Material mat, string matPath)
            {
                var baseColor = GetColor(mat, Color.white, "_BaseColor", "_Color", "baseColorFactor");
                var texProp = FirstProp(mat, "_BaseMap", "_MainTex", "baseColorTexture");
                var tex = texProp != null ? mat.GetTexture(texProp) as Texture2D : null;
                var texFile = tex ? ExportTexture(tex) : null;

                float metallic = GetFloat(mat, 0f, "_Metallic", "metallicFactor");
                float roughness = mat.HasProperty("roughnessFactor")
                    ? mat.GetFloat("roughnessFactor")
                    : 1f - GetFloat(mat, 0.5f, "_Smoothness", "_Glossiness");
                var emission = mat.HasProperty("emissiveFactor") ? mat.GetColor("emissiveFactor")
                    : mat.IsKeywordEnabled("_EMISSION") ? GetColor(mat, Color.black, "_EmissionColor")
                    : Color.black;
                bool transparent = mat.renderQueue >= 3000;
                bool cutout = mat.IsKeywordEnabled("_ALPHATEST_ON") || GetFloat(mat, 0f, "_AlphaClip") > 0.5f;
                bool useAlpha = transparent || cutout;

                Open("Material", matPath.Substring(matPath.LastIndexOf('/') + 1),
                    $"customData = {{ string unityShader = \"{Esc(mat.shader ? mat.shader.name : "")}\" }}");
                Line($"token outputs:surface.connect = <{matPath}/Surface.outputs:surface>");

                Open("Shader", "Surface");
                Line("uniform token info:id = \"UsdPreviewSurface\"");
                if (texFile != null) Line($"color3f inputs:diffuseColor.connect = <{matPath}/BaseMap.outputs:rgb>");
                else Line($"color3f inputs:diffuseColor = {Col(baseColor.linear)}");
                Line($"float inputs:metallic = {F(metallic)}");
                Line($"float inputs:roughness = {F(Mathf.Clamp01(roughness))}");
                if (emission.maxColorComponent > 0f) Line($"color3f inputs:emissiveColor = {Col(emission.linear)}");
                if (useAlpha)
                {
                    if (texFile != null) Line($"float inputs:opacity.connect = <{matPath}/BaseMap.outputs:a>");
                    else Line($"float inputs:opacity = {F(baseColor.a)}");
                }
                if (cutout) Line($"float inputs:opacityThreshold = {F(GetFloat(mat, 0.5f, "_Cutoff", "alphaCutoff"))}");
                Line("token outputs:surface");
                Close();

                if (texFile != null)
                {
                    Vector2 scale = mat.GetTextureScale(texProp), offset = mat.GetTextureOffset(texProp);
                    bool tiled = scale != Vector2.one || offset != Vector2.zero;
                    var stSource = tiled ? $"{matPath}/Tiling.outputs:result" : $"{matPath}/StReader.outputs:result";
                    var c = baseColor.linear;

                    Open("Shader", "BaseMap");
                    Line("uniform token info:id = \"UsdUVTexture\"");
                    Line($"asset inputs:file = @{texFile}@");
                    Line("token inputs:sourceColorSpace = \"sRGB\"");
                    Line($"float4 inputs:scale = ({F(c.r)}, {F(c.g)}, {F(c.b)}, {F(baseColor.a)})");
                    Line($"token inputs:wrapS = \"{Wrap(tex.wrapModeU)}\"");
                    Line($"token inputs:wrapT = \"{Wrap(tex.wrapModeV)}\"");
                    Line($"float2 inputs:st.connect = <{stSource}>");
                    Line("float3 outputs:rgb");
                    Line("float outputs:a");
                    Close();

                    if (tiled)
                    {
                        Open("Shader", "Tiling");
                        Line("uniform token info:id = \"UsdTransform2d\"");
                        Line($"float2 inputs:in.connect = <{matPath}/StReader.outputs:result>");
                        Line($"float2 inputs:scale = ({F(scale.x)}, {F(scale.y)})");
                        Line($"float2 inputs:translation = ({F(offset.x)}, {F(offset.y)})");
                        Line("float2 outputs:result");
                        Close();
                    }

                    Open("Shader", "StReader");
                    Line("uniform token info:id = \"UsdPrimvarReader_float2\"");
                    Line("string inputs:varname = \"st\"");
                    Line("float2 outputs:result");
                    Close();
                }
                Close();
            }

            /// <summary>원본이 PNG/JPG 파일이면 복사, 아니면(glb 서브에셋, 압축 텍스처 등) GPU로 읽어 PNG 인코딩.</summary>
            string ExportTexture(Texture2D tex)
            {
                if (textures.TryGetValue(tex, out var rel)) return rel;
                rel = null;
                try
                {
                    Directory.CreateDirectory(texDir);
                    var src = AssetDatabase.GetAssetPath(tex);
                    var ext = Path.GetExtension(src).ToLowerInvariant();
                    bool copy = ext == ".png" || ext == ".jpg" || ext == ".jpeg";
                    if (!copy) ext = ".png";
                    var file = Unique(Sanitize(tex.name), textureFiles) + ext;
                    var dest = Path.Combine(texDir, file);
                    if (copy) File.Copy(FileUtil.GetPhysicalPath(src), dest, true);
                    else File.WriteAllBytes(dest, ReadBackPng(tex));
                    rel = "./" + texDirName + "/" + file;
                }
                catch (Exception e) { Debug.LogWarning($"[USD] texture '{tex.name}' skipped: {e.Message}"); }
                textures[tex] = rel;
                return rel;
            }

            static byte[] ReadBackPng(Texture2D tex)
            {
                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                    throw new InvalidOperationException("GPU 없음(-nographics). 텍스처를 읽을 수 없다");
                var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var prev = RenderTexture.active;
                var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                try
                {
                    Graphics.Blit(tex, rt);
                    RenderTexture.active = rt;
                    copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                    copy.Apply();
                    return copy.EncodeToPNG();
                }
                finally
                {
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    UnityEngine.Object.DestroyImmediate(copy);
                }
            }

            static string FirstProp(Material m, params string[] props)
            {
                foreach (var p in props) if (m.HasProperty(p)) return p;
                return null;
            }

            static Color GetColor(Material m, Color fallback, params string[] props)
            {
                var p = FirstProp(m, props);
                return p != null ? m.GetColor(p) : fallback;
            }

            static float GetFloat(Material m, float fallback, params string[] props)
            {
                var p = FirstProp(m, props);
                return p != null ? m.GetFloat(p) : fallback;
            }

            static string Wrap(TextureWrapMode mode) =>
                mode == TextureWrapMode.Clamp ? "clamp" : mode == TextureWrapMode.Mirror || mode == TextureWrapMode.MirrorOnce ? "mirror" : "repeat";

            // ---------- USDA 출력 ----------

            void Line(string s)
            {
                w.Write(new string(' ', depth * 4));
                w.WriteLine(s);
            }

            void Open(string type, string name, string metadata = null)
            {
                if (metadata == null) Line($"def {type} \"{name}\"");
                else
                {
                    Line($"def {type} \"{name}\" (");
                    depth++;
                    Line(metadata);
                    depth--;
                    Line(")");
                }
                Line("{");
                depth++;
            }

            void Close()
            {
                depth--;
                Line("}");
            }

            /// <summary>큰 배열은 문자열로 모으지 않고 바로 스트림에 쓴다.</summary>
            void WriteArray<T>(string decl, IReadOnlyList<T> items, Func<T, string> fmt, string metadata = null)
            {
                w.Write(new string(' ', depth * 4));
                w.Write(decl);
                w.Write(" = [");
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) w.Write(", ");
                    w.Write(fmt(items[i]));
                }
                w.Write(']');
                if (metadata != null) w.Write($" ({metadata})");
                w.WriteLine();
            }

            static string F(float f) => f.ToString("R", CI);
            static string Vec(Vector3 v) => $"({F(v.x)}, {F(v.y)}, {F(-v.z)})"; // Z 반전
            static string Col(Color c) => $"({F(c.r)}, {F(c.g)}, {F(c.b)})";
            static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

            /// <summary>USD 식별자 규칙([A-Za-z_][A-Za-z0-9_]*)에 맞춘다. 한글 등 비ASCII는 '_'로.</summary>
            static string Sanitize(string s)
            {
                var sb = new StringBuilder(s.Length);
                foreach (var c in s) sb.Append(c < 128 && (char.IsLetterOrDigit(c) || c == '_') ? c : '_');
                if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, '_');
                return sb.ToString();
            }

            static string Unique(string name, HashSet<string> used)
            {
                var n = name;
                for (int i = 1; !used.Add(n); i++) n = $"{name}_{i}";
                return n;
            }
        }
    }
}
