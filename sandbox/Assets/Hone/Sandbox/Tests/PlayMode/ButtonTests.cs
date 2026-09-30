#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hone.Sandbox.Tests
{
    // Editor で実行する前提（UXML と PanelSettings を AssetDatabase から読む）。
    // このファイルの Button は Hone.Button を指す（Hone の入れ子の namespace なので、using UnityEngine.UIElements; があっても Hone.Button が勝つ）。
    public class ButtonTests
    {
        const string ButtonUxmlPath = "Assets/Hone/UI/Button/Button.uxml";
        const string PanelSettingsPath = "Assets/Hone/Sandbox/PanelSettings.asset";
        const string SandboxUxmlPath = "Assets/Hone/Sandbox/Sandbox.uxml";
        const int MaxWaitFrames = 60;

        readonly List<Object> m_Created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in m_Created)
                Object.Destroy(obj);
            m_Created.Clear();
        }

        static List<string> VariantClasses(VisualElement element) =>
            element.GetClasses().Where(c => c.StartsWith("hone-button--")).ToList();

        // (A) UXML の variant="outline" が hone-button と hone-button--outline のクラスになる
        [Test]
        public void Uxml_OutlineVariant_HasBlockAndVariantClass()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ButtonUxmlPath);
            Assert.IsNotNull(tree, $"{ButtonUxmlPath} was not found");

            var root = tree.Instantiate();
            var outline = root.Q<Button>(className: "hone-button--outline");

            Assert.IsNotNull(outline, "no Hone.Button with hone-button--outline");
            Assert.IsTrue(outline.ClassListContains("hone-button"));
            Assert.AreEqual(Button.Variant.Outline, outline.variant);
            CollectionAssert.AreEqual(new[] { "hone-button--outline" }, VariantClasses(outline));
        }

        // (B) variant を変えると、前の variant のクラスが外れて新しいクラスが付く。Default はクラスなし
        [Test]
        public void Variant_Change_ReplacesVariantClass()
        {
            var button = new Button { variant = Button.Variant.Outline };
            CollectionAssert.AreEqual(new[] { "hone-button--outline" }, VariantClasses(button));

            button.variant = Button.Variant.Ghost;
            CollectionAssert.AreEqual(new[] { "hone-button--ghost" }, VariantClasses(button));

            button.variant = Button.Variant.Default;
            CollectionAssert.IsEmpty(VariantClasses(button));
            Assert.IsTrue(button.ClassListContains("hone-button"), "the block class must stay");
        }

        // (C) 既定テーマの見た目（unity-button）が付いていない。基底の Button が付けるクラスを外してある
        [Test]
        public void Constructor_HasNoUnityButtonClass()
        {
            var button = new Button();

            Assert.IsFalse(button.ClassListContains("unity-button"));
            Assert.IsTrue(button.ClassListContains("hone-button"));
            Assert.IsTrue(button.ClassListContains("hone-focusable"));
            Assert.IsTrue(button.ClassListContains("hone-text"));
        }

        // (D) 基底の挙動が残っている。NavigationSubmitEvent で clicked が 1 回発火する。
        // SendEvent は panel に付いた要素でないと届かないので、PanelRenderer を 1 つ作って載せる
        [UnityTest]
        public IEnumerator Clicked_FiresOnNavigationSubmit()
        {
            var panelSettings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath));
            m_Created.Add(panelSettings);
            var go = new GameObject("ButtonTests");
            m_Created.Add(go);
            go.SetActive(false);
            var panelRenderer = go.AddComponent<PanelRenderer>();
            panelRenderer.panelSettings = panelSettings;
            panelRenderer.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SandboxUxmlPath);
            VisualElement root = null;
            panelRenderer.RegisterUIReloadCallback((renderer, element, version) => root = element);
            go.SetActive(true);

            var frames = 0;
            while ((root == null || root.panel == null) && frames < MaxWaitFrames)
            {
                frames++;
                yield return null;
            }
            Assert.IsNotNull(root?.panel, $"panel was not ready within {MaxWaitFrames} frames");

            var button = new Button { text = "submit" };
            var clicks = 0;
            button.clicked += () => clicks++;
            root.Add(button);
            yield return null;

            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = button;
                button.SendEvent(evt);
            }
            yield return null;

            Assert.AreEqual(1, clicks);
        }
    }
}
#endif
