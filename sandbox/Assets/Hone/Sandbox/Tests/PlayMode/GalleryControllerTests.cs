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

        void Register(string name, Func<string, VisualElement> factory)
        {
            m_Registered.Add(name);
            GalleryController.Register(name, factory);
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
            Register("GalleryControllerTests", text =>
            {
                received.Add(text);
                var label = new Label(text);
                label.AddToClassList(TestItemClass);
                return label;
            });
            var container = new VisualElement();

            GalleryController.Build(container, strings);

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
            Register("GalleryControllerTests.Rebuild", text =>
            {
                var label = new Label(text);
                label.AddToClassList(TestItemClass);
                return label;
            });
            var container = new VisualElement();

            GalleryController.Build(container, strings);
            GalleryController.Build(container, strings);

            Assert.AreEqual(strings.Count * 2, container.Query<Label>(className: TestItemClass).ToList().Count);
        }

        [Test]
        public void Unregister_RemovesFactory()
        {
            var calls = 0;
            Register("GalleryControllerTests.Unregister", text => { calls++; return new Label(text); });

            GalleryController.Unregister("GalleryControllerTests.Unregister");
            GalleryController.Build(new VisualElement(), LoadTestStrings());

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Build_FactoryReturnsNull_Throws()
        {
            Register("GalleryControllerTests.Null", text => null);

            Assert.Throws<InvalidOperationException>(() => GalleryController.Build(new VisualElement(), LoadTestStrings()));
        }

        [TestCase("{\"ja\": {\"name\": \"n\", \"short\": \"a\"}}")]
        [TestCase("{\"ja\": {\"name\": \"n\", \"long\": \"a\"}}")]
        [TestCase("{\"ja\": \"a\"}")]
        [TestCase("{\"ja\": {\"name\": \"n\", \"short\": 1, \"long\": \"a\"}}")]
        [TestCase("{\"ja\": {\"short\": \"a\", \"long\": \"b\"}}")]
        [TestCase("{\"ja\": {\"name\": 1, \"short\": \"a\", \"long\": \"b\"}}")]
        public void ParseTestStrings_MissingOrNonStringValue_ThrowsFormatException(string json)
        {
            Assert.Throws<FormatException>(() => GalleryController.ParseTestStrings(json));
        }

        [Test]
        public void Register_SameNameReplacesFactory()
        {
            var strings = LoadTestStrings();
            var firstCalls = 0;
            var secondCalls = 0;
            Register("GalleryControllerTests.Replace", text => { firstCalls++; return new Label(text); });
            Register("GalleryControllerTests.Replace", text => { secondCalls++; return new Label(text); });

            GalleryController.Build(new VisualElement(), strings);

            Assert.AreEqual(0, firstCalls);
            Assert.AreEqual(strings.Count * 2, secondCalls);
        }
    }
}
