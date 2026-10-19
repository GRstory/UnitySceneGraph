using System.Collections.Generic;

namespace SceneGraph.Editor
{
    public static class ThumbnailColorProvider
    {
        public static IReadOnlyDictionary<string, float> ThumbnailColors(IndexNode node)
        {
            if (node.Renderers.Length == 0) return new Dictionary<string, float>();
            var size = SceneGraphSettings.instance.thumbnailSize;
            var (onBlack, onWhite) = ThumbnailRenderer.RenderPair(node, size);
            try { return ColorExtractor.Extract(onBlack, onWhite); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(onBlack);
                UnityEngine.Object.DestroyImmediate(onWhite);
            }
        }
    }
}
