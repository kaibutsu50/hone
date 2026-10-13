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
    // ToggleGroup は Hone のもの（Hone の入れ子の namespace なので修飾しなくても Hone.ToggleGroup を指す）。Button は Unity 標準と紛らわしいので Hone.Button と修飾して書く。
    // layout と resolvedStyle を読むテストは、HoneTheme が当たった panel に attach して frame を進めてから測る。
    public class ToggleGroupTests
    {
        const string ToggleGroupUxmlPath = "Assets/Hone/UI/ToggleGroup/ToggleGroup.uxml";
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

        // panel に付いた要素でないと resolvedStyle も layout も SendEvent も効かないので、PanelRenderer を 1 つ作り、panel に付いた root を onReady に渡す
        IEnumerator CreatePanel(System.Action<VisualElement> onReady)
        {
            var panelSettings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath));
            m_Created.Add(panelSettings);
            var go = new GameObject("ToggleGroupTests");
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

        static ToggleGroup CreateGroup()
        {
            var group = new ToggleGroup();
            for (var i = 0; i < 3; i++)
                group.Add(new Hone.Button { text = "item " + i });
            return group;
        }

        // Gallery と同じ選び方。allowEmptySelection が false（既定）だと、最初の子を足した時点で基底が index 0 を選ぶので、全部外してから index を選ぶ
        static void Select(ToggleGroup group, int index)
        {
            var state = group.value;
            for (var i = 0; i < state.length; i++)
                state[i] = false;
            state[index] = true;
            group.SetValueWithoutNotify(state);
        }

        // hover の再現。evt.target を設定せずに panel の visualTree へ PointerMoveEvent を送ると、panel が座標で pick して :hover を付ける
        // 本物のマウスも同じ経路で hover を動かすので、batch 実行（unity test）を前提にする（Test Runner の窓から回すときは Game ビューの上でマウスを動かさない）
        static void MovePointerTo(VisualElement root, VisualElement target)
        {
            var center = target.worldBound.center;
            using (var evt = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = center }))
                root.panel.visualTree.SendEvent(evt);
        }

        // (A) UXML の ToggleGroup に hone-toggle-group が付き unity-toggle-button-group が付いていない。子は Hone.Button 3 つで、attach 後に横並びになる
        [UnityTest]
        public IEnumerator Uxml_HasToggleGroupClass_AndButtonsAreInARow()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ToggleGroupUxmlPath);
            Assert.IsNotNull(tree, $"{ToggleGroupUxmlPath} was not found");

            var instance = tree.Instantiate();
            var group = instance.Q<ToggleGroup>();
            Assert.IsNotNull(group, "ToggleGroup.uxml must have a ToggleGroup");
            Assert.IsTrue(group.ClassListContains("hone-toggle-group"));
            Assert.IsFalse(group.ClassListContains(UnityEngine.UIElements.ToggleButtonGroup.ussClassName));

            var buttons = group.Query<Hone.Button>().ToList();
            Assert.AreEqual(3, buttons.Count, "ToggleGroup.uxml must have 3 Hone.Button");

            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            root.Add(instance);
            yield return Settle();

            Assert.Greater(buttons[1].layout.x, buttons[0].layout.x, "the 2nd button must be to the right of the 1st");
            Assert.Greater(buttons[2].layout.x, buttons[1].layout.x, "the 3rd button must be to the right of the 2nd");
        }

        // (B) index 1 を選んだ状態で、index 1 の背景の alpha が 1、index 0 が 0。既定テーマの Button の背景に負けておらず、:checked が付いていることを測る
        [UnityTest]
        public IEnumerator SelectedButton_IsOpaque_AndOthersAreTransparent()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var group = CreateGroup();
            root.Add(group);
            yield return Settle();
            Select(group, 1);
            yield return Settle();

            var buttons = group.Query<Hone.Button>().ToList();
            Assert.AreEqual(1f, buttons[1].resolvedStyle.backgroundColor.a, "the selected button must have an opaque background");
            Assert.AreEqual(0f, buttons[0].resolvedStyle.backgroundColor.a, "an unselected button must have a transparent background");
            Assert.AreEqual(0f, buttons[2].resolvedStyle.backgroundColor.a, "an unselected button must have a transparent background");
        }

        // (C) 単一選択で index 2 に NavigationSubmitEvent を送ると、index 2 だけが選ばれ、ChangeEvent<ToggleButtonGroupState> が 1 回出る
        [UnityTest]
        public IEnumerator Submit_SelectsOnlyThatButton_AndFiresChangeOnce()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var group = CreateGroup();
            root.Add(group);
            yield return Settle();
            Assert.IsFalse(group.isMultipleSelection, "this test assumes single selection (the base default)");
            Select(group, 1);
            yield return Settle();

            var changes = 0;
            group.RegisterValueChangedCallback(evt => changes++);

            Submit(group.Query<Hone.Button>().ToList()[2]);
            yield return Settle();

            var state = group.value;
            // length は子の数ではなく常に 64（基底の状態は 64 ビット固定。Unity 6000.7.0b4 で確認）。index 2 だけが true で、ほかは全部 false
            var selected = Enumerable.Range(0, state.length).Where(i => state[i]).ToArray();
            CollectionAssert.AreEqual(new[] { 2 }, selected);
            Assert.AreEqual(1, changes);
        }

        // (D) disabled の項目に hover が付いても opacity が 0.5 のまま。disabled にする項目は、選ばれていない index 0（index 2 を選ぶ）。選ばれた項目は :checked の accent が背景を固定し、hover の有無を背景で測れない。
        // hover の再現は、enabled な項目と disabled の項目の両方で、背景が透明から muted に変わることで確かめる
        [UnityTest]
        public IEnumerator Disabled_KeepsOpacity_UnderHover()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var group = CreateGroup();
            root.Add(group);
            yield return Settle();
            Select(group, 2);
            var buttons = group.Query<Hone.Button>().ToList();
            buttons[0].SetEnabled(false);
            yield return Settle();

            var before = buttons[1].resolvedStyle.backgroundColor;
            MovePointerTo(root, buttons[1]);
            yield return Settle();
            var hovered = buttons[1].resolvedStyle.backgroundColor;
            Assert.AreNotEqual(before, hovered, "hover must change the enabled button's background (proof that hover was reproduced)");
            Assert.AreEqual(1f, hovered.a, "hover must give the enabled button the muted background");

            Assert.AreEqual(0f, buttons[0].resolvedStyle.backgroundColor.a, "the disabled button must be transparent before hover");
            MovePointerTo(root, buttons[0]);
            yield return Settle();
            Assert.AreEqual(1f, buttons[0].resolvedStyle.backgroundColor.a, "hover must reach the disabled button too (its background becomes muted)");
            Assert.AreEqual(0.5f, buttons[0].resolvedStyle.opacity, 0.001f, "a disabled button must keep opacity 0.5 under hover");
        }

        // (E) hover なしでは、disabled の項目が 0.5、enabled の項目が 1（pointer を置かない batch 実行が前提）
        [UnityTest]
        public IEnumerator Disabled_HasHalfOpacity_WithoutHover()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var group = CreateGroup();
            root.Add(group);
            yield return Settle();
            var buttons = group.Query<Hone.Button>().ToList();
            buttons[0].SetEnabled(false);
            yield return Settle();

            Assert.AreEqual(0.5f, buttons[0].resolvedStyle.opacity, 0.001f, "a disabled button must have opacity 0.5");
            Assert.AreEqual(1f, buttons[1].resolvedStyle.opacity, 0.001f, "an enabled button must have opacity 1");
            Assert.AreEqual(1f, buttons[2].resolvedStyle.opacity, 0.001f, "an enabled button must have opacity 1");
        }
    }
}
#endif
