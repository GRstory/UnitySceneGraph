using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SceneGraph.Editor.Tests
{
    /// <summary>
    /// 테스트가 만든 루트 오브젝트와 임시 에셋 폴더를 관리한다. Test Runner가 띄운 임시 씬을 쓰고,
    /// 인덱스는 여기서 만든 루트만 훑는다.
    /// </summary>
    sealed class TestScene : IDisposable
    {
        public const string TempDir = "Assets/_SceneGraphTestTemp";
        public readonly Scene Scene;
        readonly List<GameObject> roots = new List<GameObject>();

        public TestScene()
        {
            Scene = SceneManager.GetActiveScene();
            if (!AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.CreateFolder("Assets", "_SceneGraphTestTemp");
        }

        public void Dispose()
        {
            foreach (var r in roots)
                if (r != null) UnityEngine.Object.DestroyImmediate(r);
            AssetDatabase.DeleteAsset(TempDir);
        }

        public SceneIndex Index(GroupingOptions options = null) =>
            SceneIndexer.Build(roots.Where(r => r != null && r.transform.parent == null), options ?? new GroupingOptions());

        public GameObject Empty(string name, GameObject parent = null, Vector3 position = default)
        {
            var go = new GameObject(name);
            Place(go, parent, position, Vector3.one);
            return go;
        }

        public GameObject Cube(string name, GameObject parent, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            Place(go, parent, position, scale);
            return go;
        }

        /// <summary>position과 scale은 월드 기준(부모는 회전 없다고 가정).</summary>
        void Place(GameObject go, GameObject parent, Vector3 position, Vector3 scale)
        {
            if (parent != null) go.transform.SetParent(parent.transform, false);
            else roots.Add(go);
            go.transform.position = position;
            var parentScale = parent != null ? parent.transform.lossyScale : Vector3.one;
            go.transform.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, scale.z / parentScale.z);
        }

        /// <summary>root를 프리팹 에셋으로 저장하고 씬의 원본은 지운다.</summary>
        public GameObject SavePrefab(GameObject root)
        {
            var asset = PrefabUtility.SaveAsPrefabAsset(root, $"{TempDir}/{root.name}.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            Assert.AreEqual(PrefabAssetType.Regular, PrefabUtility.GetPrefabAssetType(asset));
            return asset;
        }

        public GameObject Instantiate(GameObject asset, string name, GameObject parent, Vector3 position, Vector3? scale = null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, Scene);
            go.name = name;
            Place(go, parent, position, scale ?? go.transform.localScale);
            return go;
        }

        /// <summary>단위 큐브 OBJ를 임포트한 Model 프리팹 에셋.</summary>
        public GameObject ModelAsset(string name)
        {
            var path = $"{TempDir}/{name}.obj";
            File.WriteAllText(path, CubeObj);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.NotNull(asset, "OBJ import failed");
            Assert.AreEqual(PrefabAssetType.Model, PrefabUtility.GetPrefabAssetType(asset));
            return asset;
        }

        const string CubeObj =
            "o Cube\n" +
            "v -0.5 -0.5 -0.5\nv 0.5 -0.5 -0.5\nv 0.5 0.5 -0.5\nv -0.5 0.5 -0.5\n" +
            "v -0.5 -0.5 0.5\nv 0.5 -0.5 0.5\nv 0.5 0.5 0.5\nv -0.5 0.5 0.5\n" +
            "vn 0 0 -1\nvn 0 0 1\nvn 0 -1 0\nvn 1 0 0\nvn 0 1 0\nvn -1 0 0\n" +
            "f 1//1 2//1 3//1 4//1\nf 5//2 8//2 7//2 6//2\nf 1//3 5//3 6//3 2//3\nf 2//4 6//4 7//4 3//4\nf 3//5 7//5 8//5 4//5\nf 5//6 1//6 4//6 8//6\n";
    }

    static class IndexAssert
    {
        public static IndexNode Group(SceneIndex index, string rootName)
        {
            var groups = index.Groups.Where(g => g.Root.name == rootName).ToList();
            Assert.AreEqual(1, groups.Count, $"expected one group '{rootName}'. groups: {Describe(index)}");
            return groups[0];
        }

        public static void NoGroup(SceneIndex index, string rootName) =>
            Assert.IsFalse(index.Groups.Any(g => g.Root.name == rootName), $"unexpected group '{rootName}'. groups: {Describe(index)}");

        public static string[] PartNames(IndexNode group) =>
            group.Children.Where(c => c.Kind == IndexNodeKind.Part).Select(c => c.Root.name).OrderBy(n => n).ToArray();

        public static string[] ChildGroupNames(IndexNode group) =>
            group.Children.Where(c => c.Kind == IndexNodeKind.Group).Select(c => c.Root.name).OrderBy(n => n).ToArray();

        public static string Describe(SceneIndex index) =>
            string.Join("; ", index.Groups.Select(g => $"{g.HierarchyPath}[{g.Signals}] parts={string.Join(",", PartNames(g))}"));

        public static IEnumerable<string> Names(params string[] names) => names.OrderBy(n => n);
    }
}
