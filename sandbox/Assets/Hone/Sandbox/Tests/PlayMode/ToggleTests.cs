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
using Toggle = Hone.Toggle;

namespace Hone.Sandbox.Tests
{
    // Editor で実行する前提（UXML と PanelSettings を AssetDatabase から読む）。
    // using UnityEngine.UIElements; と Hone の Toggle を併用するので、エイリアスで Toggle を Hone.Toggle にする（README の「名前の衝突」）。
    // resolvedStyle を読むテストは、HoneTheme が当たった panel に attach して frame を進めてから測る。
    public class ToggleTests
    {
        const string ToggleUxmlPath = "Assets/Hone/UI/Toggle/Toggle.uxml";
        const string PanelSettingsPath = "Assets/Hone/Sandbox/PanelSettings.asset";
        const string SandboxUxmlPath = "Assets/Hone/Sandbox/Sandbox.uxml";
        const string PressedClass = "is-pressed";
        const int MaxWaitFrames = 60;
        const int SettleFrames = 2;

        readonly List<Object> m_Created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in m_Created)
                Object.Destroy(obj);
            m_Created.Clear();
        }

        // (A) UXML の 2 つの Toggle に hone-toggle が付き unity-button が付いていない。value="true" の方は value が true で .is-pressed が付いている
        [Test]
        public void Uxml_HasToggleClasses_AndOnExampleIsPressed()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ToggleUxmlPath);
            Assert.IsNotNull(tree, $"{ToggleUxmlPath} was not found");

            var root = tree.Instantiate();
            var toggles = root.Query<Toggle>().ToList();
            Assert.AreEqual(2, toggles.Count, "Toggle.uxml must have an off and an on example");

            foreach (var element in toggles)
            {
                Assert.IsTrue(element.ClassListContains("hone-toggle"));
                Assert.IsTrue(element.ClassListContains("hone-focusable"));
                Assert.IsTrue(element.ClassListContains("hone-text"));
                Assert.IsFalse(element.ClassListContains(UnityEngine.UIElements.Button.ussClassName));
                Assert.AreEqual(element.value, element.ClassListContains(PressedClass), "value and .is-pressed must agree");
            }

            Assert.AreEqual(1, toggles.Count(t => t.value), "exactly one example must be on");
        }

        // panel に付いた要素でないと resolvedStyle も SendEvent も効かないので、PanelRenderer を 1 つ作り、panel に付いた root を onReady に渡す
        IEnumerator CreatePanel(System.Action<VisualElement> onReady)
        {
            var panelSettings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath));
            m_Created.Add(panelSettings);
            var go = new GameObject("ToggleTests");
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
            onReady(root);
        }

        static IEnumerator Settle()
        {
            for (var i = 0; i < SettleFrames; i++)
                yield return null;
        }

        static void Submit(VisualElement target)
        {
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        // (B) NavigationSubmitEvent のたびに value が反転し、ChangeEvent<bool> が 1 回ずつ出て、.is-pressed が付け外しされる
        [UnityTest]
        public IEnumerator Submit_FlipsValue_FiresChangeOnce_AndTogglesPressedClass()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var toggle = new Toggle { text = "toggle" };
            var changes = new List<(bool previous, bool next)>();
            toggle.RegisterValueChangedCallback<bool>(evt => changes.Add((evt.previousValue, evt.newValue)));
            root.Add(toggle);
            yield return Settle();

            Submit(toggle);
            yield return null;

            Assert.IsTrue(toggle.value);
            Assert.IsTrue(toggle.ClassListContains(PressedClass));
            CollectionAssert.AreEqual(new[] { (false, true) }, changes);

            Submit(toggle);
            yield return null;

            Assert.IsFalse(toggle.value);
            Assert.IsFalse(toggle.ClassListContains(PressedClass));
            CollectionAssert.AreEqual(new[] { (false, true), (true, false) }, changes);
        }

        // (C) SetValueWithoutNotify は .is-pressed を付け外しするが ChangeEvent を出さない。同じ値を value に入れても出ない
        [UnityTest]
        public IEnumerator SetValueWithoutNotify_AndSameValue_FireNoChangeEvent()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var toggle = new Toggle { text = "toggle" };
            var changes = 0;
            toggle.RegisterValueChangedCallback<bool>(evt => changes++);
            root.Add(toggle);
            yield return Settle();

            toggle.SetValueWithoutNotify(true);
            yield return null;

            Assert.IsTrue(toggle.value);
            Assert.IsTrue(toggle.ClassListContains(PressedClass));
            Assert.AreEqual(0, changes);

            toggle.value = true;
            yield return null;

            Assert.AreEqual(0, changes, "setting the same value must not fire a ChangeEvent");

            toggle.SetValueWithoutNotify(false);
            yield return null;

            Assert.IsFalse(toggle.value);
            Assert.IsFalse(toggle.ClassListContains(PressedClass));
            Assert.AreEqual(0, changes);
        }

        // (D) off のとき背景の alpha が 0、on のとき 1。既定テーマの Button の背景に負けていないことを測る
        [UnityTest]
        public IEnumerator Background_IsTransparentWhenOff_AndOpaqueWhenOn()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var toggle = new Toggle { text = "toggle" };
            root.Add(toggle);
            yield return Settle();

            Assert.AreEqual(0f, toggle.resolvedStyle.backgroundColor.a, "off must have a transparent background");

            toggle.value = true;
            yield return Settle();

            Assert.AreEqual(1f, toggle.resolvedStyle.backgroundColor.a, "on must have an opaque background");

            toggle.value = false;
            yield return Settle();

            Assert.AreEqual(0f, toggle.resolvedStyle.backgroundColor.a);
        }
    }
}
#endif
