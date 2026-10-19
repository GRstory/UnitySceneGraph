using System.Linq;
using NUnit.Framework;
using UnityEngine;
using static SceneGraph.Editor.Tests.IndexAssert;

namespace SceneGraph.Editor.Tests
{
    /// <summary>PLAN.md 3.1 예시 표 열두 경우 + 보조 규칙.</summary>
    public class GroupingRulesTests
    {
        TestScene t;

        [SetUp] public void SetUp() => t = new TestScene();
        [TearDown] public void TearDown() => t.Dispose();

        /// <summary>상판 y 0.75~0.8, 다리 4개. 루트는 빈 오브젝트.</summary>
        GameObject PlainDesk(string name, GameObject parent = null, Vector3 at = default)
        {
            var desk = t.Empty(name, parent, at);
            t.Cube("Top", desk, at + new Vector3(0, 0.775f, 0), new Vector3(1.2f, 0.05f, 0.6f));
            for (var i = 0; i < 4; i++)
                t.Cube($"Leg{i}", desk, at + new Vector3(i % 2 == 0 ? -0.55f : 0.55f, 0.375f, i < 2 ? -0.25f : 0.25f), new Vector3(0.05f, 0.75f, 0.05f));
            return desk;
        }

        GameObject PlainApple(string name, GameObject parent, Vector3 bottom)
        {
            var apple = t.Empty(name, parent, bottom);
            t.Cube("Body", apple, bottom + new Vector3(0, 0.04f, 0), Vector3.one * 0.08f);
            t.Cube("Stem", apple, bottom + new Vector3(0, 0.095f, 0), new Vector3(0.01f, 0.03f, 0.01f));
            return apple;
        }

        // 1
        [Test]
        public void Case01_NestedRegularPrefabs_BothGroups_DeskBoundsExcludeApple()
        {
            var appleAsset = t.SavePrefab(PlainApple("Apple", null, Vector3.zero));
            var deskRoot = PlainDesk("Desk");
            t.Instantiate(appleAsset, "Apple", deskRoot, new Vector3(0, 0.8f, 0));
            var deskAsset = t.SavePrefab(deskRoot);
            t.Instantiate(deskAsset, "Desk", null, Vector3.zero);

            var index = t.Index();
            var desk = Group(index, "Desk");
            var apple = Group(index, "Apple");
            Assert.AreEqual(GroupSignal.Prefab, desk.Signals);
            Assert.AreEqual(GroupSignal.Prefab, apple.Signals);
            CollectionAssert.AreEqual(Names("Leg0", "Leg1", "Leg2", "Leg3", "Top"), PartNames(desk));
            CollectionAssert.AreEqual(new[] { "Apple" }, ChildGroupNames(desk));
            CollectionAssert.AreEqual(Names("Body", "Stem"), PartNames(apple));

            Assert.IsTrue(RelationEvaluator.TryRelationBounds(apple, desk, out var deskBounds));
            Assert.AreEqual(0.8f, deskBounds.max.y, 1e-4f, "desk bounds must exclude the apple subtree");
            Assert.IsTrue(SpatialRelations.Holds(RelationType.On, apple.Bounds, deskBounds, Frame.World, new RelationOptions()));
        }

        // 2
        [Test]
        public void Case02_DeskOfRegularPartPrefabs_AllSignalAncestorsAreGroups()
        {
            var topAsset = t.SavePrefab(t.Cube("DeskTop", null, Vector3.zero, new Vector3(1.2f, 0.05f, 0.6f)));
            var legAsset = t.SavePrefab(t.Cube("Leg", null, Vector3.zero, new Vector3(0.05f, 0.75f, 0.05f)));
            var root = t.Empty("Desk");
            t.Instantiate(topAsset, "DeskTop", root, new Vector3(0, 0.775f, 0));
            for (var i = 0; i < 4; i++) t.Instantiate(legAsset, $"Leg{i}", root, new Vector3(i * 0.3f - 0.45f, 0.375f, 0));
            t.Instantiate(t.SavePrefab(root), "Desk", null, Vector3.zero);

            var index = t.Index();
            var desk = Group(index, "Desk");
            Assert.IsEmpty(PartNames(desk), "Desk has no direct parts");
            CollectionAssert.AreEqual(Names("DeskTop", "Leg0", "Leg1", "Leg2", "Leg3"), ChildGroupNames(desk));
            Assert.AreEqual(5, desk.Renderers.Length);
            Assert.Greater(desk.Bounds.size.x, 1f, "group bounds must cover child groups");
            foreach (var g in desk.Children)
                CollectionAssert.AreEqual(new[] { g.Root.name }, PartNames(g));
        }

        // 3
        [Test]
        public void Case03_EmptyDeskOfModelInstances_HeuristicStopsAtFurniture()
        {
            var model = t.ModelAsset("SM_DeskPart");
            var furniture = t.Empty("Furniture");
            t.Cube("Chair", furniture, new Vector3(3, 0.25f, 0), Vector3.one * 0.5f);
            var desk = t.Empty("Desk", furniture);
            t.Instantiate(model, "SM_DeskTop", desk, new Vector3(0, 0.775f, 0), new Vector3(1.2f, 0.05f, 0.6f));
            for (var i = 0; i < 4; i++)
                t.Instantiate(model, $"SM_DeskLeg{i}", desk, new Vector3(i * 0.3f - 0.45f, 0.375f, 0), new Vector3(0.05f, 0.75f, 0.05f));

            var index = t.Index();
            var g = Group(index, "Desk");
            Assert.AreEqual(GroupSignal.Heuristic, g.Signals);
            Assert.AreEqual(5, g.Renderers.Length);
            Assert.AreEqual(1, index.Groups.Count(), Describe(index));
            NoGroup(index, "Furniture");
            Assert.IsNull(g.Parent);
        }

        // 4
        [Test]
        public void Case04_PlainMeshRoom_DeskHeuristicGroup_AppleOnDesk()
        {
            var room = t.Empty("Room");
            var furniture = t.Empty("Furniture", room);
            var desk = PlainDesk("Desk", furniture);
            t.Cube("Apple", desk, new Vector3(0, 0.84f, 0), Vector3.one * 0.08f);
            t.Cube("Sofa", furniture, new Vector3(3, 0.4f, 0), new Vector3(2, 0.8f, 1));

            var index = t.Index();
            var g = Group(index, "Desk");
            CollectionAssert.AreEqual(Names("Apple", "Leg0", "Leg1", "Leg2", "Leg3", "Top"), PartNames(g));
            NoGroup(index, "Furniture");
            NoGroup(index, "Room");

            var apple = index.PartByRenderer[desk.transform.Find("Apple").GetComponent<Renderer>()];
            CollectionAssert.Contains(apple.NameTokens, "apple");
            CollectionAssert.Contains(g.NameTokens, "desk");
            Assert.IsTrue(RelationEvaluator.TryRelationBounds(apple, g, out var deskBounds));
            Assert.IsTrue(SpatialRelations.Holds(RelationType.On, apple.Bounds, deskBounds, Frame.World, new RelationOptions()));
        }

        // 5
        [Test]
        public void Case05_CatPrefabWithAnimatorAndModelParts_OneGroup()
        {
            var model = t.ModelAsset("CatPart");
            var root = t.Empty("Cat");
            root.AddComponent<Animator>();
            t.Instantiate(model, "CatBody", root, new Vector3(0, 0.2f, 0), new Vector3(0.5f, 0.3f, 0.2f));
            t.Instantiate(model, "Collar", root, new Vector3(0.25f, 0.3f, 0), new Vector3(0.05f, 0.1f, 0.1f));
            t.Instantiate(t.SavePrefab(root), "Cat", null, Vector3.zero);

            var index = t.Index();
            var cat = Group(index, "Cat");
            Assert.AreEqual(GroupSignal.Prefab | GroupSignal.Animator, cat.Signals);
            Assert.AreEqual(1, index.Groups.Count(), Describe(index));
            Assert.AreEqual(2, cat.Children.Count(c => c.Kind == IndexNodeKind.Part));
        }

        GameObject Shelf(bool rigidbodyBooks)
        {
            var root = t.Empty("Shelf");
            t.Cube("Board", root, new Vector3(0, 0.5f, 0), new Vector3(1, 0.05f, 0.3f));
            if (rigidbodyBooks)
            {
                for (var i = 0; i < 3; i++)
                {
                    var book = t.Cube($"Book{i}", root, new Vector3(i * 0.1f - 0.1f, 0.625f, 0), new Vector3(0.05f, 0.2f, 0.15f));
                    book.AddComponent<Rigidbody>();
                }
            }
            else
            {
                var book = t.Empty("Book_01", root);
                t.Cube("Body", book, new Vector3(0, 0.625f, 0), new Vector3(0.05f, 0.2f, 0.15f));
                t.Cube("Cover", book, new Vector3(0.03f, 0.625f, 0), new Vector3(0.01f, 0.2f, 0.15f));
            }
            return t.Instantiate(t.SavePrefab(root), "Shelf", null, Vector3.zero);
        }

        // 6
        [Test]
        public void Case06_ShelfPrefabWithRigidbodyBooks_BookGroupsUnderShelf()
        {
            Shelf(rigidbodyBooks: true);
            var index = t.Index();
            var shelf = Group(index, "Shelf");
            CollectionAssert.AreEqual(new[] { "Board" }, PartNames(shelf));
            CollectionAssert.AreEqual(Names("Book0", "Book1", "Book2"), ChildGroupNames(shelf));
            foreach (var book in shelf.Children.Where(c => c.Kind == IndexNodeKind.Group))
            {
                Assert.AreEqual(GroupSignal.Rigidbody, book.Signals);
                CollectionAssert.AreEqual(new[] { book.Root.name }, PartNames(book));
            }
        }

        /// <summary>같은 높이의 옆 책이 선반 그룹 상자 윗면을 올려도 on은 받침 파츠(Board) 단위로 본다.</summary>
        [Test]
        public void Case06_BookOnShelf_WithSameHeightSiblingBooks()
        {
            Shelf(rigidbodyBooks: true);
            var index = t.Index();
            var shelf = Group(index, "Shelf");
            var book = Group(index, "Book0");
            var engine = new QueryEngine(index, new RelationOptions(), _ => null);
            Assert.IsTrue(engine.RelationHolds(RelationType.On, book, shelf, Frame.World));
        }

        // 7
        [Test]
        public void Case07_ShelfPrefabWithPlainBook_HeuristicBookGroupInsideShelf()
        {
            Shelf(rigidbodyBooks: false);
            var index = t.Index();
            var shelf = Group(index, "Shelf");
            var book = Group(index, "Book_01");
            Assert.AreEqual(GroupSignal.Heuristic, book.Signals);
            Assert.AreSame(shelf, book.Parent);
            CollectionAssert.AreEqual(new[] { "Board" }, PartNames(shelf));
            CollectionAssert.AreEqual(Names("Body", "Cover"), PartNames(book));
        }

        // 8
        [Test]
        public void Case08_SingleSwordUnderArmature_NoGroup()
        {
            var root = t.Empty("Cat");
            root.AddComponent<Animator>();
            t.Cube("CatBody", root, new Vector3(0, 0.2f, 0), new Vector3(0.5f, 0.3f, 0.2f));
            var armature = t.Empty("Armature", root);
            var spine = t.Empty("Spine", armature);
            var hand = t.Empty("Hand", spine);
            t.Cube("Sword", hand, new Vector3(0.3f, 0.3f, 0), new Vector3(0.02f, 0.3f, 0.02f));
            t.Instantiate(t.SavePrefab(root), "Cat", null, Vector3.zero);

            var index = t.Index();
            var cat = Group(index, "Cat");
            Assert.AreEqual(1, index.Groups.Count(), Describe(index));
            CollectionAssert.AreEqual(Names("CatBody", "Sword"), PartNames(cat));
        }

        // 9
        [Test]
        public void Case09_MeshHolderInsidePrefab_NoDuplicateGroup()
        {
            var root = t.Empty("Desk");
            var mesh = t.Empty("Mesh", root);
            t.Cube("Top", mesh, new Vector3(0, 0.775f, 0), new Vector3(1.2f, 0.05f, 0.6f));
            for (var i = 0; i < 4; i++) t.Cube($"Leg{i}", mesh, new Vector3(i * 0.3f - 0.45f, 0.375f, 0), new Vector3(0.05f, 0.75f, 0.05f));
            t.Instantiate(t.SavePrefab(root), "Desk", null, Vector3.zero);

            var index = t.Index();
            var desk = Group(index, "Desk");
            NoGroup(index, "Mesh");
            Assert.AreEqual(1, index.Groups.Count(), Describe(index));
            Assert.AreEqual(5, PartNames(desk).Length);
        }

        // 10
        [Test]
        public void Case10_RoomPrefabWith300Renderers_PrefabGroupDroppedAndSplit()
        {
            var root = t.Empty("Room");
            for (var i = 0; i < 30; i++)
            {
                var table = t.Empty($"Table_{i}", root, new Vector3(i % 6 * 5f, 0, i / 6 * 5f));
                for (var j = 0; j < 10; j++)
                    t.Cube($"Piece{j}", table, table.transform.position + new Vector3(j * 0.1f, 0.5f, 0), Vector3.one * 0.08f);
            }
            t.Instantiate(t.SavePrefab(root), "Room", null, Vector3.zero);

            var index = t.Index();
            NoGroup(index, "Room");
            Assert.AreEqual(30, index.Groups.Count(), Describe(index));
            foreach (var g in index.Groups)
            {
                StringAssert.StartsWith("Table_", g.Root.name);
                Assert.AreEqual(GroupSignal.Heuristic, g.Signals);
                Assert.AreEqual(10, PartNames(g).Length);
                Assert.IsNull(g.Parent);
            }
        }

        // 11
        [Test]
        public void Case11_GroupRootTagOverridesRigidbodyLegs()
        {
            var root = PlainDesk("Desk");
            root.AddComponent<SemanticTag>().GroupRoot = true;
            foreach (Transform child in root.transform)
                if (child.name.StartsWith("Leg")) child.gameObject.AddComponent<Rigidbody>();
            t.Instantiate(t.SavePrefab(root), "Desk", null, Vector3.zero);

            var index = t.Index();
            var desk = Group(index, "Desk");
            Assert.IsTrue(desk.Signals.HasFlag(GroupSignal.Manual));
            Assert.AreEqual(1, index.Groups.Count(), Describe(index));
            Assert.AreEqual(5, PartNames(desk).Length);
        }

        [Test]
        public void Case11_GroupRootInsideRoomPrefab_RoomGroupStillExists()
        {
            var room = t.Empty("Room");
            t.Cube("Floor", room, Vector3.zero, new Vector3(3, 0.05f, 3));
            var desk = PlainDesk("Desk", room);
            desk.AddComponent<SemanticTag>().GroupRoot = true;
            t.Instantiate(t.SavePrefab(room), "Room", null, Vector3.zero);

            var index = t.Index();
            var roomGroup = Group(index, "Room");
            Assert.AreSame(roomGroup, Group(index, "Desk").Parent);
        }

        // 12
        [Test]
        public void Case12_CubeUsedAsBoxInsideDeskPrefab()
        {
            var root = PlainDesk("Desk");
            t.Cube("Cube", root, new Vector3(0, 0.85f, 0), Vector3.one * 0.1f);
            t.Instantiate(t.SavePrefab(root), "Desk", null, Vector3.zero);

            var index = t.Index();
            var desk = Group(index, "Desk");
            CollectionAssert.Contains(PartNames(desk), "Cube");
            Assert.AreEqual(1, index.Groups.Count(), Describe(index));
        }

        [Test]
        public void Case12_CubeWithGroupRootTagAndOverrideNouns_SeparateBoxGroup()
        {
            var root = PlainDesk("Desk");
            var cube = t.Cube("Cube", root, new Vector3(0, 0.85f, 0), Vector3.one * 0.1f);
            var tag = cube.AddComponent<SemanticTag>();
            tag.GroupRoot = true;
            tag.OverridesNameTokens = new[] { "box" };
            t.Instantiate(t.SavePrefab(root), "Desk", null, Vector3.zero);

            var index = t.Index();
            var desk = Group(index, "Desk");
            var box = Group(index, "Cube");
            Assert.AreSame(desk, box.Parent);
            CollectionAssert.AreEqual(new[] { "box" }, box.NameTokens);
            CollectionAssert.DoesNotContain(PartNames(desk), "Cube");
        }

        [Test]
        public void HeuristicDoesNotClimbAboveSignalGroups_Pets()
        {
            var catAsset = t.SavePrefab(PlainApple("Cat", null, Vector3.zero));
            var pets = t.Empty("Pets");
            t.Instantiate(catAsset, "Cat1", pets, Vector3.zero);
            t.Instantiate(catAsset, "Cat2", pets, new Vector3(1, 0, 0));

            var index = t.Index();
            NoGroup(index, "Pets");
            Assert.AreEqual(2, index.Groups.Count(), Describe(index));
        }

        [Test]
        public void LodGroup_KeepsOnlyLod0Renderers()
        {
            var root = t.Empty("Tree");
            var lod0 = t.Cube("LOD0", root, Vector3.zero, Vector3.one);
            var lod1 = t.Cube("LOD1", root, Vector3.zero, Vector3.one * 3f);
            root.AddComponent<LODGroup>().SetLODs(new[]
            {
                new LOD(0.5f, new[] { lod0.GetComponent<Renderer>() }),
                new LOD(0.1f, new[] { lod1.GetComponent<Renderer>() }),
            });

            var index = t.Index();
            var tree = Group(index, "Tree");
            Assert.AreEqual(GroupSignal.LodGroup, tree.Signals);
            CollectionAssert.AreEqual(new[] { "LOD0" }, PartNames(tree));
            Assert.AreEqual(1f, tree.Bounds.size.x, 1e-4f);
        }

        [Test]
        public void SplitChildren_StopsHeuristicAndIsNotASignal()
        {
            var room = t.Empty("Room");
            var holder = t.Empty("Holder", room);
            holder.AddComponent<Rigidbody>();
            holder.AddComponent<SemanticTag>().SplitChildren = true;
            var a = t.Empty("A", holder);
            t.Cube("A1", a, Vector3.zero, Vector3.one * 0.1f);
            t.Cube("A2", a, new Vector3(0.2f, 0, 0), Vector3.one * 0.1f);
            var b = t.Empty("B", holder);
            t.Cube("B1", b, new Vector3(1, 0, 0), Vector3.one * 0.1f);
            t.Cube("B2", b, new Vector3(1.2f, 0, 0), Vector3.one * 0.1f);

            var index = t.Index();
            NoGroup(index, "Holder");
            NoGroup(index, "Room");
            Group(index, "A");
            Group(index, "B");
        }
    }
}
