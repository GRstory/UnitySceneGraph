using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SceneGraph.Editor
{
    /// <summary>
    /// PreviewRenderUtility로 노드 서브트리 렌더러 전체를 렌더한다. M1은 정면 1장.
    /// 2D 스프라이트는 M5.
    /// </summary>
    public static class ThumbnailRenderer
    {
        const float FieldOfView = 30f;
        static readonly Vector3 ViewDirection = new Vector3(0f, -0.35f, 1f).normalized;

        public static Texture2D Render(IndexNode node, int size, Color background) =>
            Render(node.Renderers, node.Bounds, size, background);

        /// <summary>색 추출용으로 검정/흰 배경 두 장.</summary>
        public static (Texture2D onBlack, Texture2D onWhite) RenderPair(IndexNode node, int size)
        {
            var bounds = node.Bounds;
            return (Render(node.Renderers, bounds, size, Color.black), Render(node.Renderers, bounds, size, Color.white));
        }

        public static Texture2D Render(IReadOnlyList<Renderer> renderers, Bounds bounds, int size, Color background)
        {
            var preview = new PreviewRenderUtility();
            var baked = new List<Mesh>();
            try
            {
                var cam = preview.camera;
                var radius = Mathf.Max(bounds.extents.magnitude, 1e-3f);
                var distance = radius / Mathf.Sin(FieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
                cam.fieldOfView = FieldOfView;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = background;
                cam.transform.SetPositionAndRotation(bounds.center - ViewDirection * distance, Quaternion.LookRotation(ViewDirection));
                cam.nearClipPlane = Mathf.Max(distance - radius * 1.5f, distance * 0.01f);
                cam.farClipPlane = distance + radius * 2f;

                preview.lights[0].intensity = 1.2f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
                preview.lights[1].intensity = 0.6f;
                preview.lights[1].transform.rotation = Quaternion.Euler(340f, 218f, 177f);
                preview.ambientColor = new Color(0.35f, 0.35f, 0.35f);

                preview.BeginStaticPreview(new Rect(0, 0, size, size));
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    var mats = r.sharedMaterials;
                    if (mats.Length == 0) continue;
                    Mesh mesh;
                    Matrix4x4 matrix;
                    switch (r)
                    {
                        case MeshRenderer _:
                            var mf = r.GetComponent<MeshFilter>();
                            mesh = mf != null ? mf.sharedMesh : null;
                            matrix = r.localToWorldMatrix;
                            break;
                        case SkinnedMeshRenderer smr:
                            if (smr.sharedMesh == null) continue;
                            mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                            smr.BakeMesh(mesh, true);
                            baked.Add(mesh);
                            matrix = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                            break;
                        default:
                            continue;
                    }
                    if (mesh == null) continue;
                    for (var sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        var mat = mats[Mathf.Min(sub, mats.Length - 1)];
                        if (mat != null) preview.DrawMesh(mesh, matrix, mat, sub);
                    }
                }
                preview.Render(true);
                return preview.EndStaticPreview();
            }
            finally
            {
                preview.Cleanup();
                foreach (var m in baked) Object.DestroyImmediate(m);
            }
        }
    }
}
