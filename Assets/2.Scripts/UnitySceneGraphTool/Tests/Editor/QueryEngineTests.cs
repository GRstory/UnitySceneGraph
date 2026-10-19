using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SceneGraph.Editor.Tests
{
    /// <summary>PLAN.md 4.2 점수, 조상 제외 bounds, 부모-자식 중복 제거, 3.5 스키마.</summary>
    public class QueryEngineTests
    {
        TestScene t;
        Dictionary<string, Dictionary<string, float>> colors;

        [SetUp]
        public void SetUp()
        {
            t = new TestScene();
            colors = new Dictionary<string, Dictionary<string, float>>();
        }

        [TearDown] public void TearDown() => t.Dispose();

        QueryResponse Run(string json)
        {
            var engine = new QueryEngine(t.Index(), new RelationOptions(),
                n => colors.TryGetValue(n.Root.name, out var c) ? c : new Dictionary<string, float>());
            return engine.Run(SceneQuery.Parse(json));
        }

        static string[] Paths(QueryResponse r) => r.Results.Select(x => x.Node.HierarchyPath).ToArray();

        GameObject Desk(string name = "Desk")
        {
            var desk = t.Empty(name);
            t.Cube("Top", desk, new Vector3(0, 0.775f, 0), new Vector3(1.2f, 0.05f, 0.6f));
            t.Cube("Leg0", desk, new Vector3(-0.55f, 0.375f, 0), new Vector3(0.05f, 0.75f, 0.05f));
            t.Cube("Leg1", desk, new Vector3(0.55f, 0.375f, 0), new Vector3(0.05f, 0.75f, 0.05f));
            return desk;
        }

        GameObject Apple(string name, GameObject parent, Vector3 bottom, string color, float ratio)
        {
            colors[name] = new Dictionary<string, float> { [color] = ratio, ["black"] = 1f - ratio };
            return t.Cube(name, parent, bottom + new Vector3(0, 0.04f, 0), Vector3.one * 0.08f);
        }

        const string RedAppleOnDesk =
            "{\"target\":{\"nouns\":[\"apple\"],\"colors\":[\"red\"]},\"relations\":[{\"type\":\"on\",\"object\":{\"nouns\":[\"desk\"]}}]}";

        [Test]
        public void Score_IsNounTimesColorTimesRelation()
        {
            var desk = Desk();
            Apple("Apple_A", desk, new Vector3(0, 0.8f, 0), "red", 0.6f);

            var r = Run(RedAppleOnDesk);
            Assert.AreEqual(1, r.Results.Count);
            var top = r.Results[0];
            Assert.AreEqual("Desk/Apple_A", top.Node.HierarchyPath);
            Assert.AreEqual(1f, top.NounScore);
            Assert.AreEqual(0.6f, top.ColorScore, 1e-5f);
            Assert.AreEqual(1f, top.RelationScore);
            Assert.AreEqual(0.6f, top.Score, 1e-5f);
            CollectionAssert.AreEqual(new[] { "apple" }, top.MatchedNouns);
            CollectionAssert.AreEqual(new[] { "red" }, top.MatchedColors);
            Assert.AreEqual("Desk", top.MatchedRelations.Single().Object.HierarchyPath);
        }

        [Test]
        public void RelationScore_UsesBestObjectCandidateScore()
        {
            var desk = Desk();
            colors["Desk"] = new Dictionary<string, float> { ["brown"] = 0.5f };
            Apple("Apple_A", desk, new Vector3(0, 0.8f, 0), "red", 1f);
            var json = "{\"target\":{\"nouns\":[\"apple\"]},\"relations\":[{\"type\":\"on\",\"object\":{\"nouns\":[\"desk\"],\"colors\":[\"brown\"]}}]}";
            Assert.AreEqual(0.5f, Run(json).Results.Single().Score, 1e-5f);
        }

        [Test]
        public void M1_RedAppleOnDesk_PicksOnlyThatApple()
        {
            var desk = Desk();
            Apple("Apple_A", desk, new Vector3(-0.3f, 0.8f, 0), "red", 0.7f);
            Apple("Apple_C", null, new Vector3(0.3f, 0.8f, 0), "green", 0.9f);
            Apple("Apple_B", null, new Vector3(2f, 0f, 0), "red", 0.8f);

            CollectionAssert.AreEqual(new[] { "Desk/Apple_A" }, Paths(Run(RedAppleOnDesk)));
        }

        /// <summary>같은 책상 그룹 안에 초록 사과가 형제 파츠로 있어도 받침 파츠(Top) 단위라 on이 성립한다.</summary>
        [Test]
        public void M1_RedAppleOnDesk_WithSiblingAppleOnSameDesk()
        {
            var desk = Desk();
            Apple("Apple_A", desk, new Vector3(-0.3f, 0.8f, 0), "red", 0.7f);
            Apple("Apple_C", desk, new Vector3(0.3f, 0.8f, 0), "green", 0.9f);

            CollectionAssert.AreEqual(new[] { "Desk/Apple_A" }, Paths(Run(RedAppleOnDesk)));
        }

        [Test]
        public void M1_AppleThatContainsLeaf_PicksOnlyLeafyApple()
        {
            var leafy = t.Empty("Apple");
            t.Cube("Body", leafy, new Vector3(0, 0.04f, 0), Vector3.one * 0.08f);
            t.Cube("Leaf", leafy, new Vector3(0, 0.09f, 0), new Vector3(0.03f, 0.005f, 0.02f));
            var plain = t.Empty("Apple", null, new Vector3(1, 0, 0));
            t.Cube("Body", plain, new Vector3(1, 0.04f, 0), Vector3.one * 0.08f);

            var r = Run("{\"target\":{\"nouns\":[\"apple\"]},\"relations\":[{\"type\":\"contains\",\"object\":{\"nouns\":[\"leaf\"]}}]}");
            Assert.AreEqual(1, r.Results.Count, string.Join(", ", Paths(r)));
            Assert.AreSame(leafy, r.Results[0].Node.Root);
            Assert.AreEqual(IndexNodeKind.Group, r.Results[0].Node.Kind);
        }

        [Test]
        public void On_FourLegDeskGroup_NameOnlyOnGroupRoot()
        {
            var desk = t.Empty("Desk");
            t.Cube("Top", desk, new Vector3(0, 0.775f, 0), new Vector3(1.2f, 0.05f, 0.6f));
            for (var i = 0; i < 4; i++)
                t.Cube($"Leg{i}", desk, new Vector3(i % 2 == 0 ? -0.55f : 0.55f, 0.375f, i < 2 ? -0.25f : 0.25f), new Vector3(0.05f, 0.75f, 0.05f));
            Apple("Apple_A", null, new Vector3(0.2f, 0.8f, 0), "red", 1f);

            var r = Run(RedAppleOnDesk);
            CollectionAssert.AreEqual(new[] { "Apple_A" }, Paths(r));
            Assert.AreEqual("Desk", r.Results[0].MatchedRelations.Single().Object.HierarchyPath, "path is the object node, not the support part");
        }

        [Test]
        public void Above_UsesSupportPartsNotGroupBox()
        {
            // 책상 그룹에 높은 기둥이 있어 그룹 상자 윗면(2.0)은 램프보다 높다. 상판 기준으로는 램프가 위에 떠 있다.
            var desk = t.Empty("Desk");
            t.Cube("Top", desk, new Vector3(0, 0.775f, 0), new Vector3(1.2f, 0.05f, 0.6f));
            t.Cube("Pole", desk, new Vector3(0.55f, 1f, 0), new Vector3(0.05f, 2f, 0.05f));
            t.Cube("Lamp", null, new Vector3(-0.3f, 1.5f, 0), Vector3.one * 0.2f);

            var index = t.Index();
            var engine = new QueryEngine(index, new RelationOptions(), _ => null);
            var lamp = index.All.Single(n => n.Root.name == "Lamp");
            Assert.IsTrue(engine.RelationHolds(RelationType.Above, lamp, index.GroupByRoot[desk], Frame.World));
            Assert.IsFalse(engine.RelationHolds(RelationType.On, lamp, index.GroupByRoot[desk], Frame.World));
        }

        [Test]
        public void Below_UsesSupportPartsNotGroupBox()
        {
            // 선반 그룹에 바닥까지 닿는 기둥이 있어 그룹 상자 아랫면(0)은 상자보다 낮다. 판재 기준으로는 상자가 아래에 있다.
            var shelf = t.Empty("Shelf");
            t.Cube("Board", shelf, new Vector3(0, 1.5f, 0), new Vector3(1f, 0.05f, 0.4f));
            t.Cube("Post", shelf, new Vector3(0.48f, 1f, 0), new Vector3(0.04f, 2f, 0.04f));
            t.Cube("Box", null, new Vector3(-0.3f, 0.5f, 0), Vector3.one * 0.2f);

            var index = t.Index();
            var engine = new QueryEngine(index, new RelationOptions(), _ => null);
            var box = index.All.Single(n => n.Root.name == "Box");
            Assert.IsTrue(engine.RelationHolds(RelationType.Below, box, index.GroupByRoot[shelf], Frame.World));
            Assert.IsFalse(engine.RelationHolds(RelationType.Above, box, index.GroupByRoot[shelf], Frame.World));
        }

        [Test]
        public void NounBelowThreshold_IsDropped()
        {
            Desk();
            Assert.IsEmpty(Run("{\"target\":{\"nouns\":[\"banana\"]}}").Results);
        }

        [Test]
        public void ParentAndChildWithSameNoun_KeepsParentOnly()
        {
            var desk = Desk();
            t.Cube("DeskDrawer", desk, new Vector3(0, 0.65f, 0), new Vector3(0.4f, 0.1f, 0.4f));
            CollectionAssert.AreEqual(new[] { "Desk" }, Paths(Run("{\"target\":{\"nouns\":[\"desk\"]}}")));
        }

        [Test]
        public void ParentAndChildWithDifferentNouns_KeepsBoth()
        {
            var desk = Desk();
            Apple("Apple_A", desk, new Vector3(0, 0.8f, 0), "red", 1f);
            var r = Run("{\"target\":{\"nouns\":[\"desk\",\"apple\"]}}");
            CollectionAssert.AreEquivalent(new[] { "Desk", "Desk/Apple_A" }, Paths(r));
        }

        [Test]
        public void AncestorExclusion_AppleParentedUnderDeskTop()
        {
            var desk = Desk();
            var top = desk.transform.Find("Top").gameObject;
            Apple("Apple_A", top, new Vector3(0, 0.8f, 0), "red", 1f);
            var index = t.Index();
            var deskGroup = index.GroupByRoot[desk];
            var apple = index.PartByRenderer[GameObject.Find("Apple_A").GetComponent<Renderer>()];

            Assert.AreEqual(0.88f, deskGroup.Bounds.max.y, 1e-4f, "raw desk bounds include the apple");
            Assert.IsTrue(RelationEvaluator.TryRelationBounds(apple, deskGroup, out var excluded));
            Assert.AreEqual(0.8f, excluded.max.y, 1e-4f);

            var engine = new QueryEngine(index, new RelationOptions(), _ => new Dictionary<string, float>());
            Assert.IsTrue(engine.RelationHolds(RelationType.On, apple, deskGroup, Frame.World));
            Assert.IsFalse(engine.RelationHolds(RelationType.Inside, apple, deskGroup, Frame.World));
            // contains는 조상 제외 없이 계층으로 본다
            Assert.IsTrue(engine.RelationHolds(RelationType.Contains, deskGroup, apple, Frame.World));
            // 대상이 object의 조상이면 다른 관계는 성립하지 않는다
            Assert.IsFalse(engine.RelationHolds(RelationType.On, deskGroup, apple, Frame.World));
        }

        [Test]
        public void ObjectFrame_InFrontOfSofa()
        {
            var sofa = t.Cube("Sofa", null, Vector3.zero, new Vector3(2, 0.8f, 1));
            sofa.transform.rotation = Quaternion.Euler(0, 90, 0); // sofa가 +x를 본다
            t.Cube("Cat", null, new Vector3(2, 0.1f, 0), Vector3.one * 0.2f);
            t.Cube("Cat", null, new Vector3(0, 0.1f, 2), Vector3.one * 0.2f).name = "Cat2";

            var world = Run("{\"target\":{\"nouns\":[\"cat\"]},\"relations\":[{\"type\":\"in_front_of\",\"object\":{\"nouns\":[\"sofa\"]}}]}");
            CollectionAssert.AreEqual(new[] { "Cat2" }, Paths(world));

            var obj = Run("{\"target\":{\"nouns\":[\"cat\"]},\"relations\":[{\"type\":\"in_front_of\",\"object\":{\"nouns\":[\"sofa\"]}}]," +
                          "\"frame\":{\"type\":\"object\",\"object\":{\"nouns\":[\"sofa\"]}}}");
            CollectionAssert.AreEqual(new[] { "Cat" }, Paths(obj));
        }

        [Test]
        public void Limit_AndSortByScore()
        {
            for (var i = 0; i < 5; i++) Apple($"Apple_{i}", null, new Vector3(i, 0, 0), "red", 0.1f * (i + 1));
            var r = Run("{\"target\":{\"nouns\":[\"apple\"],\"colors\":[\"red\"]},\"limit\":3}");
            CollectionAssert.AreEqual(new[] { "Apple_4", "Apple_3", "Apple_2" }, Paths(r));
        }

        [Test]
        public void Parse_FlatFormFromMilestoneEqualsTargetForm()
        {
            var flat = SceneQuery.Parse("{\"nouns\":[\"Apple\"],\"colors\":[\"red\"],\"relations\":[{\"type\":\"on\",\"object\":{\"nouns\":[\"desk\",\"table\"]}}]}");
            var wrapped = SceneQuery.Parse(RedAppleOnDesk);
            CollectionAssert.AreEqual(new[] { "apple" }, flat.Target.Nouns);
            CollectionAssert.AreEqual(wrapped.Target.Colors, flat.Target.Colors);
            Assert.AreEqual(RelationType.On, flat.Target.Relations.Single().Type);
            CollectionAssert.AreEqual(new[] { "desk", "table" }, flat.Target.Relations[0].Object.Nouns);
            Assert.IsNull(flat.Frame);
        }

        [Test]
        public void Parse_FrameAndErrors()
        {
            var q = SceneQuery.Parse("{\"target\":{\"nouns\":[\"cat\"]},\"frame\":{\"type\":\"world\",\"forward\":\"-x\",\"up\":\"+y\"},\"limit\":2}");
            Assert.AreEqual(FrameType.World, q.Frame.Type);
            Assert.AreEqual("-x", q.Frame.Forward);
            Assert.AreEqual(2, q.Limit);
            Assert.AreEqual(FrameType.SceneCamera, SceneQuery.Parse("{\"target\":{},\"frame\":{\"type\":\"sceneCamera\"}}").Frame.Type);
            Assert.Throws<System.FormatException>(() => SceneQuery.Parse("{\"target\":{},\"relations\":[{\"type\":\"beside\",\"object\":{}}]}"));
            Assert.Throws<System.FormatException>(() => SceneQuery.Parse("{\"target\":"));
        }

        [Test]
        public void ResponseJson_HasPathScoreAndMatched()
        {
            var desk = Desk();
            Apple("Apple_A", desk, new Vector3(0, 0.8f, 0), "red", 0.6f);
            var json = Run(RedAppleOnDesk).ToJson();
            var parsed = (Dictionary<string, object>)MiniJson.Parse(json);
            var result = (Dictionary<string, object>)((List<object>)parsed["results"])[0];
            Assert.AreEqual("Desk/Apple_A", result["path"]);
            Assert.AreEqual(0.6, (double)result["score"], 1e-3);
            var matched = (Dictionary<string, object>)result["matched"];
            var rel = (Dictionary<string, object>)((List<object>)matched["relations"])[0];
            Assert.AreEqual("on", rel["type"]);
            Assert.AreEqual("Desk", rel["path"]);
            Assert.IsTrue(parsed.ContainsKey("indexStatus"));
        }
    }

    public class ColorAndTokenTests
    {
        [TestCase(1f, 0f, 0f, "red")]
        [TestCase(0.55f, 0.05f, 0.05f, "red")]
        [TestCase(1f, 0.5f, 0f, "orange")]
        [TestCase(1f, 1f, 0f, "yellow")]
        [TestCase(0f, 0.8f, 0f, "green")]
        [TestCase(0f, 0.9f, 0.9f, "cyan")]
        [TestCase(0f, 0f, 1f, "blue")]
        [TestCase(0.5f, 0f, 1f, "purple")]
        [TestCase(1f, 0.4f, 0.8f, "pink")]
        [TestCase(1f, 1f, 1f, "white")]
        [TestCase(0.5f, 0.5f, 0.5f, "gray")]
        [TestCase(0.02f, 0.02f, 0.02f, "black")]
        [TestCase(0.4f, 0.25f, 0.1f, "brown")]
        public void ColorBuckets(float r, float g, float b, string expected) =>
            Assert.AreEqual(expected, ColorExtractor.Bucket(new Color(r, g, b)));

        [Test]
        public void ColorExtractor_TwoBackgroundMask()
        {
            var onBlack = new Texture2D(2, 1);
            var onWhite = new Texture2D(2, 1);
            try
            {
                onBlack.SetPixels(new[] { Color.red, Color.black });
                onWhite.SetPixels(new[] { Color.red, Color.white });
                var ratios = ColorExtractor.Extract(onBlack, onWhite);
                Assert.AreEqual(1f, ratios["red"]);
                Assert.IsFalse(ratios.ContainsKey("black"));
            }
            finally
            {
                Object.DestroyImmediate(onBlack);
                Object.DestroyImmediate(onWhite);
            }
        }

        [TestCase("Red Apple-5095be0d5a3d29ad8496c103223a1fdb", new[] { "red", "apple" })]
        [TestCase("SM_DeskTop", new[] { "desk", "top" })]
        [TestCase("RedApple01", new[] { "red", "apple" })]
        [TestCase("Books (3)", new[] { "books", "book" })]
        [TestCase("Cube (3)", new[] { "cube" })]
        public void NameTokenizer_Splits(string name, string[] expected) =>
            CollectionAssert.AreEqual(expected, NameTokenizer.Tokenize(name));
    }
}
