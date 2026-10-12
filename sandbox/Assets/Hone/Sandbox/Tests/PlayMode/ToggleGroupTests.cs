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
    // このファイルの ToggleGroup と Button は Hone のもの。Hone の入れ子の namespace なので修飾しなくても Hone.Button を指すが、明示して書く。
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

        // Gallery と同じ選び方。基底は未選択を許さない設定のとき index 0 を選んでいることがあるので、全部外してから index を選ぶ
        static void Select(ToggleGroup group, int index)
        {
            var state = group.value;
            for (var i = 0; i < state.length; i++)
                state[i] = false;
            state[index] = true;
            group.SetValueWithoutNotify(state);
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
            Select(group, 1);
            yield return Settle();

            var changes = 0;
            group.RegisterValueChangedCallback(evt => changes++);

            Submit(group.Query<Hone.Button>().ToList()[2]);
            yield return Settle();

            var state = group.value;
            // length は子の数ではなく常に 64（基底の状態は 64 ビット固定）。index 2 だけが true で、ほかは全部 false
            var selected = Enumerable.Range(0, state.length).Where(i => state[i]).ToArray();
            CollectionAssert.AreEqual(new[] { 2 }, selected);
            Assert.AreEqual(1, changes);
        }
    }
}
#endif
