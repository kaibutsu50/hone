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
    // このファイルの Switch は Hone.Switch を指す。
    // resolvedStyle を読むテストは、HoneTheme が当たった panel に attach して frame を進めてから測る。
    public class SwitchTests
    {
        const string SwitchUxmlPath = "Assets/Hone/UI/Switch/Switch.uxml";
        const string PanelSettingsPath = "Assets/Hone/Sandbox/PanelSettings.asset";
        const string SandboxUxmlPath = "Assets/Hone/Sandbox/Sandbox.uxml";
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

        static VisualElement TrackOf(Switch element) => element.Q(className: "hone-switch__track");
        static VisualElement ThumbOf(Switch element) => element.Q(className: "hone-switch__thumb");

        // (A) UXML の 2 つの Switch に hone-switch が付き unity-toggle が付いていない。track と thumb のクラスも付け替わっている
        [Test]
        public void Uxml_HasSwitchClasses_AndNoToggleClasses()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SwitchUxmlPath);
            Assert.IsNotNull(tree, $"{SwitchUxmlPath} was not found");

            var root = tree.Instantiate();
            var switches = root.Query<Switch>().ToList();
            Assert.AreEqual(2, switches.Count, "Switch.uxml must have an off and an on example");
            Assert.AreEqual(1, switches.Count(s => s.value), "one of the examples must be on");

            foreach (var element in switches)
            {
                Assert.IsTrue(element.ClassListContains("hone-switch"));
                Assert.IsTrue(element.ClassListContains("hone-text"));
                Assert.IsFalse(element.ClassListContains(UnityEngine.UIElements.Toggle.ussClassName));

                var track = TrackOf(element);
                Assert.IsNotNull(track, "no .hone-switch__track");
                Assert.AreSame(element, track.parent, "track must be a direct child of the Switch");
                Assert.IsFalse(track.ClassListContains(UnityEngine.UIElements.Toggle.inputUssClassName));

                var thumb = ThumbOf(element);
                Assert.IsNotNull(thumb, "no .hone-switch__thumb");
                Assert.AreSame(track, thumb.parent, "thumb must be a direct child of the track");
                Assert.IsFalse(thumb.ClassListContains(UnityEngine.UIElements.Toggle.checkmarkUssClassName));
            }
        }

        // panel に付いた要素でないと resolvedStyle も SendEvent も効かないので、PanelRenderer を 1 つ作り、panel に付いた root を onReady に渡す
        IEnumerator CreatePanel(System.Action<VisualElement> onReady)
        {
            var panelSettings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath));
            m_Created.Add(panelSettings);
            var go = new GameObject("SwitchTests");
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

        // (B) value を切り替えると track の justifyContent が FlexStart / FlexEnd に変わり、backgroundColor が on と off で異なる
        [UnityTest]
        public IEnumerator Value_Toggles_TrackJustifyAndBackground()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var element = new Switch { label = "switch" };
            root.Add(element);
            yield return Settle();
            var track = TrackOf(element);

            Assert.AreEqual(Justify.FlexStart, track.resolvedStyle.justifyContent);
            var offColor = track.resolvedStyle.backgroundColor;

            element.value = true;
            yield return Settle();

            Assert.AreEqual(Justify.FlexEnd, track.resolvedStyle.justifyContent);
            Assert.AreNotEqual(offColor, track.resolvedStyle.backgroundColor, "on and off must have different track colors");

            element.value = false;
            yield return Settle();

            Assert.AreEqual(Justify.FlexStart, track.resolvedStyle.justifyContent);
            Assert.AreEqual(offColor, track.resolvedStyle.backgroundColor);
        }

        // (C) 基底の挙動が残っている。NavigationSubmitEvent で value が反転し、ChangeEvent<bool> が 1 回出る
        [UnityTest]
        public IEnumerator Submit_FlipsValue_AndFiresChangeOnce()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var element = new Switch { label = "switch" };
            var changes = 0;
            element.RegisterValueChangedCallback(evt => changes++);
            root.Add(element);
            yield return Settle();

            Submit(element);
            yield return null;

            Assert.IsTrue(element.value);
            Assert.AreEqual(1, changes);

            Submit(element);
            yield return null;

            Assert.IsFalse(element.value);
            Assert.AreEqual(2, changes);
        }

        // (D) フォーカスすると track の border の色が変わり、border の幅と track の幅は変わらない
        [UnityTest]
        public IEnumerator Focus_ChangesTrackBorderColor_NotWidth()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var element = new Switch { label = "switch" };
            root.Add(element);
            yield return Settle();
            var track = TrackOf(element);

            var colorBefore = track.resolvedStyle.borderTopColor;
            var borderWidthBefore = track.resolvedStyle.borderTopWidth;
            var widthBefore = track.layout.width;

            element.Focus();
            yield return Settle();

            Assert.AreNotEqual(colorBefore, track.resolvedStyle.borderTopColor, "focus must change the track border color");
            Assert.AreEqual(borderWidthBefore, track.resolvedStyle.borderTopWidth);
            Assert.AreEqual(widthBefore, track.layout.width);
        }
    }
}
#endif
