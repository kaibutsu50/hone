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
            GalleryController.Register("GalleryControllerTests", text =>
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
        public void Register_SameNameReplacesFactory()
        {
            var strings = LoadTestStrings();
            var firstCalls = 0;
            var secondCalls = 0;
            GalleryController.Register("GalleryControllerTests.Replace", text => { firstCalls++; return new Label(text); });
            GalleryController.Register("GalleryControllerTests.Replace", text => { secondCalls++; return new Label(text); });

            GalleryController.Build(new VisualElement(), strings);

            Assert.AreEqual(0, firstCalls);
            Assert.AreEqual(strings.Count * 2, secondCalls);
        }
    }
}
