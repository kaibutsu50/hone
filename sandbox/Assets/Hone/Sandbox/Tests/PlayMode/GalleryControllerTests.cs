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

            CollectionAssert.AreEquivalent(
                new[] { "ja", "ko", "zh-hans", "zh-hant", "ar", "th", "emoji", "de-long", "en" }, scripts);
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

        [TestCase("{\"ja\": {\"short\": \"a\"}}")]
        [TestCase("{\"ja\": {\"long\": \"a\"}}")]
        [TestCase("{\"ja\": \"a\"}")]
        [TestCase("{\"ja\": {\"short\": 1, \"long\": \"a\"}}")]
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
