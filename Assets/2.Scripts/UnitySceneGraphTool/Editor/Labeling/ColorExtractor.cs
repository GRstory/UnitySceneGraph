using System.Collections.Generic;
using UnityEngine;

namespace SceneGraph.Editor
{
    /// <summary>
    /// 썸네일 물체 픽셀의 HSV 히스토그램 → 12색 버킷 비율. PLAN.md 3.3, 4장.
    /// </summary>
    public static class ColorExtractor
    {
        public static readonly string[] Buckets =
            { "red", "orange", "yellow", "green", "cyan", "blue", "purple", "pink", "white", "gray", "black", "brown" };

        /// <summary>알파 > 0 픽셀만 센다.</summary>
        public static Dictionary<string, float> Extract(Texture2D tex)
        {
            var pixels = tex.GetPixels32();
            var counts = new Dictionary<string, int>();
            var total = 0;
            foreach (var p in pixels)
            {
                if (p.a == 0) continue;
                Count(counts, p, ref total);
            }
            return Normalize(counts, total);
        }

        /// <summary>
        /// 같은 장면을 검정/흰 배경으로 두 번 찍은 것. 두 장에서 색이 같은 픽셀만 물체로 친다.
        /// URP 프리뷰는 알파가 항상 1로 나올 수 있어서 이 방식으로 마스크를 만든다.
        /// </summary>
        public static Dictionary<string, float> Extract(Texture2D onBlack, Texture2D onWhite)
        {
            var a = onBlack.GetPixels32();
            var b = onWhite.GetPixels32();
            var counts = new Dictionary<string, int>();
            var total = 0;
            for (var i = 0; i < a.Length && i < b.Length; i++)
            {
                var diff = Mathf.Max(Mathf.Abs(a[i].r - b[i].r), Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b));
                if (diff > 24) continue;
                Count(counts, a[i], ref total);
            }
            return Normalize(counts, total);
        }

        static void Count(Dictionary<string, int> counts, Color32 c, ref int total)
        {
            var bucket = Bucket(c);
            counts[bucket] = counts.TryGetValue(bucket, out var n) ? n + 1 : 1;
            total++;
        }

        static Dictionary<string, float> Normalize(Dictionary<string, int> counts, int total)
        {
            var result = new Dictionary<string, float>();
            if (total == 0) return result;
            foreach (var pair in counts) result[pair.Key] = (float)pair.Value / total;
            return result;
        }

        public static string Bucket(Color c)
        {
            Color.RGBToHSV(c, out var h, out var s, out var v);
            h *= 360f;
            if (v < 0.18f) return "black";
            if (s < 0.18f) return v > 0.8f ? "white" : v < 0.3f ? "black" : "gray";
            if (h >= 10f && h < 50f && v < 0.55f) return "brown";
            var redHue = h >= 345f || h < 15f;
            if (redHue && s < 0.5f && v > 0.7f) return "pink";
            if (redHue) return "red";
            if (h < 40f) return "orange";
            if (h < 70f) return "yellow";
            if (h < 165f) return "green";
            if (h < 195f) return "cyan";
            if (h < 255f) return "blue";
            if (h < 290f) return "purple";
            return "pink";
        }
    }
}
