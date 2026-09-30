#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using Hone.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hone.Sandbox.Tests
{
    // Editor で実行する前提（PanelSettings と UXML を AssetDatabase から読む）。
    // 移動は NavigationMoveEvent を SendEvent で送る。入力層（Input System、EventSystem）を通らないので、EventSystem の有無ではテストを分けない。
    // フォーカスの変更は非同期なので、Focus() や SendEvent のあとは 1 frame 進めてから focusedElement を読む
    public class FocusScopeTests
    {
        const string PanelSettingsPath = "Assets/Hone/Sandbox/PanelSettings.asset";
        const string UxmlPath = "Assets/Hone/Sandbox/Sandbox.uxml";
        const int MaxWaitFrames = 60;

        // out-top / scope(in-1, in-2, in-3) / out-bottom を縦に並べる。全部同じ幅なので Up / Down は必ず重なる
        class Fixture
        {
            public VisualElement Root;
            public Button OutTop;
            public FocusScope Scope;
            public Button In1;
            public Button In2;
            public Button In3;
            public Button OutBottom;

            public IPanel Panel => Root.panel;
            public Focusable Focused => Panel.focusController.focusedElement;
        }

        readonly List<Object> m_Created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in m_Created)
                Object.Destroy(obj);
            m_Created.Clear();
        }

        // PanelSettings の複製ごとに別の panel になる（BackStackTests と同じ理由）
        IEnumerator CreatePanel(Action<VisualElement> onReady)
        {
            var panelSettings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath));
            m_Created.Add(panelSettings);
            var go = new GameObject("FocusScopeTests");
            m_Created.Add(go);
            go.SetActive(false);
            var panelRenderer = go.AddComponent<PanelRenderer>();
            panelRenderer.panelSettings = panelSettings;
            panelRenderer.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
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

        // layout が済んで worldBound が使えるまで待つ
        static IEnumerator WaitForLayout(params VisualElement[] elements)
        {
            var frames = 0;
            while (frames < MaxWaitFrames && Array.Exists(elements, e => !(e.worldBound.height > 0)))
            {
                frames++;
                yield return null;
            }
            foreach (var e in elements)
                Assert.Greater(e.worldBound.height, 0f, $"{e.name} was not laid out within {MaxWaitFrames} frames");
        }

        IEnumerator BuildFixture(Action<Fixture> onReady, bool trap = true, bool autoFocus = true)
        {
            var f = new Fixture();
            yield return CreatePanel(r => f.Root = r);
            f.OutTop = new Button { name = "out-top", text = "out-top" };
            f.Scope = new FocusScope { name = "scope", trap = trap, autoFocus = autoFocus };
            f.In1 = new Button { name = "in-1", text = "in-1" };
            f.In2 = new Button { name = "in-2", text = "in-2" };
            f.In3 = new Button { name = "in-3", text = "in-3" };
            f.OutBottom = new Button { name = "out-bottom", text = "out-bottom" };
            f.Scope.Add(f.In1);
            f.Scope.Add(f.In2);
            f.Scope.Add(f.In3);
            f.Root.Add(f.OutTop);
            f.Root.Add(f.Scope);
            f.Root.Add(f.OutBottom);
            yield return WaitForLayout(f.OutTop, f.In1, f.In2, f.In3, f.OutBottom);
            onReady(f);
        }

        static void SendMove(VisualElement target, NavigationMoveEvent.Direction direction)
        {
            using (var evt = NavigationMoveEvent.GetPooled(direction))
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        // 要素にフォーカスを当て、確定するまで待つ
        static IEnumerator FocusAndWait(Fixture f, VisualElement element)
        {
            element.Focus();
            yield return null;
            Assert.AreSame(element, f.Focused, $"could not focus {element.name}");
        }

        // (A)
        [UnityTest]
        public IEnumerator Trap_AtEdge_StaysInScope()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);

            yield return FocusAndWait(f, f.In3);
            SendMove(f.In3, NavigationMoveEvent.Direction.Down);
            yield return null;
            Assert.AreSame(f.In3, f.Focused, "Down from the last element must not leave the scope");

            yield return FocusAndWait(f, f.In1);
            SendMove(f.In1, NavigationMoveEvent.Direction.Up);
            yield return null;
            Assert.AreSame(f.In1, f.Focused, "Up from the first element must not leave the scope");
        }

        // (B)
        [UnityTest]
        public IEnumerator Trap_WithNeighbor_MovesToNeighbor()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);

            yield return FocusAndWait(f, f.In1);
            SendMove(f.In1, NavigationMoveEvent.Direction.Down);
            yield return null;
            Assert.AreSame(f.In2, f.Focused);

            SendMove(f.In2, NavigationMoveEvent.Direction.Up);
            yield return null;
            Assert.AreSame(f.In1, f.Focused);
        }

        // (B) の Next / Previous。DFS 順で前後に動き、端では折り返さない
        [UnityTest]
        public IEnumerator Trap_NextAndPrevious_FollowOrderWithoutWrapping()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);

            yield return FocusAndWait(f, f.In1);
            SendMove(f.In1, NavigationMoveEvent.Direction.Next);
            yield return null;
            Assert.AreSame(f.In2, f.Focused);

            SendMove(f.In2, NavigationMoveEvent.Direction.Previous);
            yield return null;
            Assert.AreSame(f.In1, f.Focused);

            SendMove(f.In1, NavigationMoveEvent.Direction.Previous);
            yield return null;
            Assert.AreSame(f.In1, f.Focused, "Previous from the first element must not wrap");

            yield return FocusAndWait(f, f.In3);
            SendMove(f.In3, NavigationMoveEvent.Direction.Next);
            yield return null;
            Assert.AreSame(f.In3, f.Focused, "Next from the last element must not wrap");
        }

        // 候補の条件（enabledInHierarchy、display）。無効な要素と隠れた要素は飛ばす
        [UnityTest]
        public IEnumerator Trap_SkipsDisabledAndHiddenElements()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);
            var hidden = new Button { name = "hidden", text = "hidden" };
            f.Scope.Insert(1, hidden);
            hidden.style.display = DisplayStyle.None;
            f.In2.SetEnabled(false);
            yield return WaitForLayout(f.In1, f.In3);

            yield return FocusAndWait(f, f.In1);
            SendMove(f.In1, NavigationMoveEvent.Direction.Next);
            yield return null;

            Assert.AreSame(f.In3, f.Focused);
        }

        // delegatesFocus の TextField は全体で 1 つの候補。内側の要素も候補にすると、Next / Previous が TextField の中で止まる
        [UnityTest]
        public IEnumerator Trap_TextField_NextAndPreviousPassThrough()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);
            var textField = new TextField { name = "text" };
            f.Scope.Insert(1, textField);
            yield return WaitForLayout(f.In1, f.In2, textField);

            yield return FocusAndWait(f, f.In1);
            SendMove(f.In1, NavigationMoveEvent.Direction.Next);
            yield return null;
            Assert.AreSame(textField, f.Focused);

            SendMove((VisualElement)f.Focused, NavigationMoveEvent.Direction.Next);
            yield return null;
            Assert.AreSame(f.In2, f.Focused, "Next must leave the TextField");

            SendMove(f.In2, NavigationMoveEvent.Direction.Previous);
            yield return null;
            Assert.AreSame(textField, f.Focused);

            SendMove((VisualElement)f.Focused, NavigationMoveEvent.Direction.Previous);
            yield return null;
            Assert.AreSame(f.In1, f.Focused, "Previous must leave the TextField");
        }

        // 入れ子の scope。target を含む最も内側の trap が処理する（外側が先に動かさない）
        [UnityTest]
        public IEnumerator Trap_Nested_InnermostHandlesMove()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);
            var inner = new FocusScope { name = "inner", trap = true };
            var i1 = new Button { name = "i-1", text = "i-1" };
            var i2 = new Button { name = "i-2", text = "i-2" };
            inner.Add(i1);
            inner.Add(i2);
            f.Scope.Insert(1, inner);
            yield return WaitForLayout(i1, i2);

            yield return FocusAndWait(f, i2);
            SendMove(i2, NavigationMoveEvent.Direction.Down);
            yield return null;
            Assert.AreSame(i2, f.Focused, "the inner trap must keep the focus even though the outer scope has an element below");

            // 内側が trap でなければ外側が処理する
            inner.trap = false;
            SendMove(i2, NavigationMoveEvent.Direction.Down);
            yield return null;
            Assert.AreSame(f.In2, f.Focused);
        }

        // (C)
        [UnityTest]
        public IEnumerator Activate_FocusesFirstFocusableDescendant()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x, trap: false);

            f.Scope.Activate();
            yield return null;

            Assert.AreSame(f.In1, f.Focused);
        }

        [UnityTest]
        public IEnumerator Activate_AutoFocusFalse_DoesNotMoveFocus()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x, autoFocus: false);
            yield return FocusAndWait(f, f.OutTop);

            f.Scope.Activate();
            yield return null;

            Assert.AreSame(f.OutTop, f.Focused);
        }

        [UnityTest]
        public IEnumerator FocusFirst_FocusesFirstFocusableDescendant()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x, autoFocus: false);

            f.Scope.FocusFirst();
            yield return null;

            Assert.AreSame(f.In1, f.Focused);
        }

        // panel に attach された時も autoFocus に従う
        [UnityTest]
        public IEnumerator AttachToPanel_AutoFocus_FocusesFirstDescendant()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x, autoFocus: false);
            var scope = new FocusScope { name = "attached" };
            var a1 = new Button { name = "a-1", text = "a-1" };
            var a2 = new Button { name = "a-2", text = "a-2" };
            scope.Add(a1);
            scope.Add(a2);

            f.Root.Add(scope);
            yield return WaitForLayout(a1, a2);
            yield return null;

            Assert.AreSame(a1, f.Focused);
        }

        // (D)
        [UnityTest]
        public IEnumerator Deactivate_RestoresPreviousFocus()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);
            yield return FocusAndWait(f, f.OutBottom);

            f.Scope.Activate();
            yield return null;
            Assert.AreSame(f.In1, f.Focused);

            f.Scope.Deactivate();
            yield return null;

            Assert.AreSame(f.OutBottom, f.Focused);
        }

        // Activate を二度呼んでも、scope の中へ移ったあとの要素を「元の要素」にしない
        [UnityTest]
        public IEnumerator Activate_CalledTwice_KeepsOriginalElement()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);
            yield return FocusAndWait(f, f.OutBottom);

            f.Scope.Activate();
            yield return null;
            f.Scope.Activate();
            yield return null;
            f.Scope.Deactivate();
            yield return null;

            Assert.AreSame(f.OutBottom, f.Focused);
        }

        // (E)
        [UnityTest]
        public IEnumerator Deactivate_PreviousDetached_DoesNotThrow()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);
            yield return FocusAndWait(f, f.OutBottom);
            f.Scope.Activate();
            yield return null;
            f.OutBottom.RemoveFromHierarchy();
            yield return null;

            Assert.DoesNotThrow(() => f.Scope.Deactivate());
            yield return null;

            Assert.AreSame(f.In1, f.Focused, "nothing to restore, so the focus stays where it was");
        }

        [UnityTest]
        public IEnumerator Deactivate_WithoutActivate_DoesNothing()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x, autoFocus: false);
            yield return FocusAndWait(f, f.OutTop);

            Assert.DoesNotThrow(() => f.Scope.Deactivate());
            yield return null;

            Assert.AreSame(f.OutTop, f.Focused);
        }

        // (F) trap=false では scope の外へ出られる（既定のナビゲーション）
        [UnityTest]
        public IEnumerator NoTrap_DefaultNavigationLeavesScope()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x, trap: false);

            yield return FocusAndWait(f, f.In3);
            SendMove(f.In3, NavigationMoveEvent.Direction.Down);
            yield return null;

            Assert.AreSame(f.OutBottom, f.Focused);
        }
    }
}
#endif
