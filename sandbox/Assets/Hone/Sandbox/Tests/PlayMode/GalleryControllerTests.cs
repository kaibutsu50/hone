using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hone.Sandbox.Gallery;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Tests
{
    public class GalleryControllerTests
    {
        const string TestItemClass = "gallery-test-item";

        // Register は static で、Domain Reload 無効の Editor では Play をまたいで残る。Gallery の撮影に混ざらないよう、登録した name を必ず消す
        readonly List<string> m_Registered = new List<string>();

        [TearDown]
        public void TearDown()
        {
            foreach (var name in m_Registered)
                GalleryController.Unregister(name);
            m_Registered.Clear();
        }

        void Register(string name, Func<string, VisualElement> factory, ComponentDescription? description = null)
        {
            m_Registered.Add(name);
            GalleryController.Register(name, factory, description);
        }

        // Editor で実行する前提（Player では Assets/ のファイルを直接読めない）
        static IReadOnlyList<ScriptStrings> LoadTestStrings()
        {
            var path = Path.Combine(Application.dataPath, "Hone/Sandbox/Gallery/TestStrings.json");
            return GalleryController.ParseTestStrings(File.ReadAllText(path));
        }

        [Test]
        public void TestStrings_HasAllScripts()
        {
            var scripts = LoadTestStrings().Select(s => s.Script).ToList();

            // 順序込みで見る。dropdown の初期値は en 以外の先頭（ja）なので、並び順が既定の言語を決める
            CollectionAssert.AreEqual(
                new[] { "en", "ja", "ko", "zh-hans", "zh-hant", "ar", "th", "de-long" }, scripts);
        }

        [Test]
        public void ParseTestStrings_ReadsName()
        {
            var strings = GalleryController.ParseTestStrings("{\"ja\": {\"name\": \"Japanese\", \"short\": \"a\", \"long\": \"b\"}}");

            Assert.AreEqual("ja", strings[0].Script);
            Assert.AreEqual("Japanese", strings[0].Name);
            Assert.AreEqual("a", strings[0].Short);
            Assert.AreEqual("b", strings[0].Long);
        }

        [Test]
        public void Pick_ReturnsBaseThenSelected()
        {
            // en を後ろに置き、all の並び順ではなく en が先頭になることを確かめる
            var all = GalleryController.ParseTestStrings(
                "{\"ko\": {\"name\": \"Korean\", \"short\": \"a\", \"long\": \"b\"}, \"en\": {\"name\": \"English\", \"short\": \"c\", \"long\": \"d\"}}");

            var picked = GalleryController.Pick(all, "ko");

            CollectionAssert.AreEqual(new[] { "en", "ko" }, picked.Select(s => s.Script).ToList());
        }

        [TestCase("en")]
        [TestCase("xx")]
        public void Pick_InvalidScript_ThrowsArgumentException(string script)
        {
            Assert.Throws<ArgumentException>(() => GalleryController.Pick(LoadTestStrings(), script));
        }

        [Test]
        public void Pick_NoBaseScript_ThrowsArgumentException()
        {
            var all = GalleryController.ParseTestStrings("{\"ja\": {\"name\": \"Japanese\", \"short\": \"a\", \"long\": \"b\"}}");

            Assert.Throws<ArgumentException>(() => GalleryController.Pick(all, "ja"));
        }

        [Test]
        public void Build_CallsRegisteredFactoryForEveryScriptAndKind()
        {
            var strings = LoadTestStrings();
            var received = new List<string>();
            const string name = "GalleryControllerTests";
            Register(name, text =>
            {
                received.Add(text);
                var label = new Label(text);
                label.AddToClassList(TestItemClass);
                return label;
            });
            var container = new VisualElement();

            GalleryController.Build(container, name, strings);

            var expected = strings.SelectMany(s => new[] { s.Short, s.Long }).ToList();
            Assert.AreEqual(expected.Count, received.Count);
            CollectionAssert.AreEquivalent(expected, received);
            var items = container.Query<Label>(className: TestItemClass).ToList();
            Assert.AreEqual(expected.Count, items.Count);
            CollectionAssert.AreEquivalent(expected, items.Select(l => l.text));
        }

        [Test]
        public void Build_RebuildsWithoutDuplicatingElements()
        {
            var strings = LoadTestStrings();
            const string name = "GalleryControllerTests.Rebuild";
            Register(name, text =>
            {
                var label = new Label(text);
                label.AddToClassList(TestItemClass);
                return label;
            });
            var container = new VisualElement();

            GalleryController.Build(container, name, strings);
            GalleryController.Build(container, name, strings);

            Assert.AreEqual(strings.Count * 2, container.Query<Label>(className: TestItemClass).ToList().Count);
        }

        [Test]
        public void Unregister_RemovesFactory()
        {
            const string name = "GalleryControllerTests.Unregister";
            Register(name, text => new Label(text));

            GalleryController.Unregister(name);

            CollectionAssert.DoesNotContain(GalleryController.Names, name);
            Assert.Throws<ArgumentException>(() => GalleryController.Build(new VisualElement(), name, LoadTestStrings()));
        }

        [Test]
        public void Build_FactoryReturnsNull_Throws()
        {
            const string name = "GalleryControllerTests.Null";
            Register(name, text => null);

            Assert.Throws<InvalidOperationException>(() => GalleryController.Build(new VisualElement(), name, LoadTestStrings()));
        }

        [Test]
        public void Build_OnlyBuildsTheNamedEntry()
        {
            var strings = LoadTestStrings();
            var targetCalls = 0;
            var otherCalls = 0;
            // 先に登録したほうではなく、後から登録したほうを Build する（先頭のエントリを作る実装を通さないため）
            Register("GalleryControllerTests.Other", text => { otherCalls++; return new Label(text); });
            Register("GalleryControllerTests.Target", text => { targetCalls++; return new Label(text); });
            var container = new VisualElement();

            GalleryController.Build(container, "GalleryControllerTests.Target", strings);

            Assert.AreEqual(strings.Count * 2, targetCalls);
            Assert.AreEqual(0, otherCalls);
            Assert.IsNull(container.Q(className: GalleryController.ColumnClass("GalleryControllerTests.Other")));
        }

        [Test]
        public void Build_AnotherName_ReplacesTheColumn()
        {
            const string first = "GalleryControllerTests.SwitchFrom";
            const string second = "GalleryControllerTests.SwitchTo";
            Register(first, text => new Label(text));
            Register(second, text => new Label(text));
            var container = new VisualElement();

            GalleryController.Build(container, first, LoadTestStrings());
            GalleryController.Build(container, second, LoadTestStrings());

            Assert.AreEqual(1, container.childCount);
            Assert.IsTrue(container[0].ClassListContains(GalleryController.ColumnClass(second)));
        }

        [Test]
        public void Names_FollowsRegistrationOrder_ReRegisterKeepsPosition()
        {
            // 辞書順とは逆の順に登録する（名前で並べ替える実装を通さないため）
            const string first = "GalleryControllerTests.Zeta";
            const string second = "GalleryControllerTests.Alpha";
            Register(first, text => new Label(text));
            Register(second, text => new Label(text));
            var before = GalleryController.Names.ToList();

            Register(first, text => new Label(text));

            var after = GalleryController.Names.ToList();
            Assert.Less(after.IndexOf(first), after.IndexOf(second));
            CollectionAssert.AreEqual(before, after);
        }

        [Test]
        public void Build_UnknownName_ThrowsArgumentExceptionAndKeepsContent()
        {
            var container = new VisualElement();
            var existing = new VisualElement();
            container.Add(existing);

            Assert.Throws<ArgumentException>(() =>
                GalleryController.Build(container, "GalleryControllerTests.NotRegistered", LoadTestStrings()));
            Assert.AreEqual(1, container.childCount);
            Assert.AreSame(existing, container[0]);
        }

        [TestCase("{\"ja\": {\"name\": \"n\", \"short\": \"a\"}}")]
        [TestCase("{\"ja\": {\"name\": \"n\", \"long\": \"a\"}}")]
        [TestCase("{\"ja\": \"a\"}")]
        [TestCase("{\"ja\": {\"name\": \"n\", \"short\": 1, \"long\": \"a\"}}")]
        [TestCase("{\"ja\": {\"short\": \"a\", \"long\": \"b\"}}")]
        [TestCase("{\"ja\": {\"name\": 1, \"short\": \"a\", \"long\": \"b\"}}")]
        public void ParseTestStrings_MissingOrNonStringValue_ThrowsFormatException(string json)
        {
            var e = Assert.Throws<FormatException>(() => GalleryController.ParseTestStrings(json));
            StringAssert.Contains("'ja'", e.Message);
        }

        [Test]
        public void Build_WithPick_PutsBaseRowThenSelectedRow()
        {
            const string name = "GalleryControllerTests.Pick";
            Register(name, text => new Label(text));
            var container = new VisualElement();

            GalleryController.Build(container, name, GalleryController.Pick(LoadTestStrings(), "ko"));

            var column = container.Q(className: GalleryController.ColumnClass(name));
            var tags = column.Query<Label>(className: "gallery-tag").ToList().Select(l => l.text).ToList();
            CollectionAssert.AreEqual(new[] { "en", "ko" }, tags);
        }

        static List<string> DescriptionTexts(VisualElement container)
        {
            var description = container.Q(className: "gallery-description");
            Assert.IsNotNull(description);
            return description.Query<Label>(className: "gallery-description__text").ToList().Select(l => l.text).ToList();
        }

        [Test]
        public void Build_WithDescription_ShowsVariantsAndUxml()
        {
            const string name = "GalleryControllerTests.Description";
            Register(name, text => new Label(text), new ComponentDescription("Default / Outline", "<hone:Button />"));
            var container = new VisualElement();

            GalleryController.Build(container, name, LoadTestStrings());

            CollectionAssert.AreEqual(new[] { "Default / Outline", "<hone:Button />" }, DescriptionTexts(container));
        }

        [TestCase(null)]
        [TestCase("")]
        public void Build_WithDescriptionWithoutVariants_ShowsNone(string variants)
        {
            const string name = "GalleryControllerTests.NoVariants";
            Register(name, text => new Label(text), new ComponentDescription(variants, "<hone:Dialog />"));
            var container = new VisualElement();

            GalleryController.Build(container, name, LoadTestStrings());

            CollectionAssert.AreEqual(new[] { "なし", "<hone:Dialog />" }, DescriptionTexts(container));
        }

        [Test]
        public void Build_WithoutDescription_HasNoDescription()
        {
            const string name = "GalleryControllerTests.NoDescription";
            Register(name, text => new Label(text));
            var container = new VisualElement();

            GalleryController.Build(container, name, LoadTestStrings());

            Assert.IsNull(container.Q(className: "gallery-description"));
        }

        [Test]
        public void Register_SameNameReplacesFactory()
        {
            var strings = LoadTestStrings();
            var firstCalls = 0;
            var secondCalls = 0;
            const string name = "GalleryControllerTests.Replace";
            Register(name, text => { firstCalls++; return new Label(text); });
            Register(name, text => { secondCalls++; return new Label(text); });

            GalleryController.Build(new VisualElement(), name, strings);

            Assert.AreEqual(0, firstCalls);
            Assert.AreEqual(strings.Count * 2, secondCalls);
        }
    }
}
