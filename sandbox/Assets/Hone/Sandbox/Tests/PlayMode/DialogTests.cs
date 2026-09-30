#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    // このファイルの Button は Hone.Button を指す（Hone の入れ子の namespace なので、using UnityEngine.UIElements; があっても Hone.Button が勝つ）。
    // Dialog は Dialog.uxml の使用例（タイトル、本文、cancel と ok の Button）を root に複製して使う。
    // フォーカスの変更は非同期なので、Focus()・Open()・Close()・SendEvent のあとは 1 frame 進めてから focusedElement を読む
    public class DialogTests
    {
        const string DialogUxmlPath = "Assets/Hone/UI/Dialog/Dialog.uxml";
        const string PanelSettingsPath = "Assets/Hone/Sandbox/PanelSettings.asset";
        const string SandboxUxmlPath = "Assets/Hone/Sandbox/Sandbox.uxml";
        const int MaxWaitFrames = 60;

        // root に outside-1 と outside-2（Dialog の外の focusable）を縦に並べる。Dialog は各テストが AddDialog で足す
        class Fixture
        {
            public VisualElement Root;
            public Button Outside1;
            public Button Outside2;

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

        // PanelRenderer を 1 つ作り、UI が読み込まれたら root を返す。
        // PanelSettings の複製ごとに別の panel になる。同じ asset を共有すると panel とそのフォーカス・BackStack の状態がテスト間で残る
        IEnumerator CreatePanel(Action<VisualElement> onReady)
        {
            var panelSettings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath));
            m_Created.Add(panelSettings);
            var go = new GameObject("DialogTests");
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

        IEnumerator BuildFixture(Action<Fixture> onReady)
        {
            var f = new Fixture();
            yield return CreatePanel(r => f.Root = r);
            f.Outside1 = new Button { name = "outside-1", text = "outside-1" };
            f.Outside2 = new Button { name = "outside-2", text = "outside-2" };
            f.Root.Add(f.Outside1);
            f.Root.Add(f.Outside2);
            yield return WaitForLayout(f.Outside1, f.Outside2);
            onReady(f);
        }

        // Dialog.uxml を root の直下に複製し、足した Dialog を返す（閉じた状態）
        static Dialog AddDialog(VisualElement root)
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DialogUxmlPath);
            Assert.IsNotNull(tree, $"{DialogUxmlPath} was not found");
            var before = root.Query<Dialog>().ToList();
            tree.CloneTree(root);
            var added = root.Query<Dialog>().ToList().Except(before).ToList();
            Assert.AreEqual(1, added.Count, "Dialog.uxml must contain exactly one Dialog");
            return added[0];
        }

        static Button CancelButton(Dialog dialog) => dialog.Q<Button>("cancel");
        static Button OkButton(Dialog dialog) => dialog.Q<Button>("ok");

        // 要素にフォーカスを当て、確定するまで待つ
        static IEnumerator FocusAndWait(Fixture f, VisualElement element)
        {
            element.Focus();
            yield return null;
            Assert.AreSame(element, f.Focused, $"could not focus {element.name}");
        }

        static void SendMove(VisualElement target, NavigationMoveEvent.Direction direction)
        {
            using (var evt = NavigationMoveEvent.GetPooled(direction))
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        static void SendCancel(VisualElement target)
        {
            using (var evt = NavigationCancelEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        // overlay の左上の内側（content に重ならない位置）を押して離す。
        // 押したままにすると、ポインタの押下状態が後のテストに残るので、PointerUp も送る
        static void Click(VisualElement target)
        {
            var position = target.worldBound.min + new Vector2(2, 2);
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = position, button = 0, clickCount = 1 }))
            {
                down.target = target;
                target.SendEvent(down);
            }
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = position, button = 0, clickCount = 1 }))
            {
                up.target = target;
                target.SendEvent(up);
            }
        }

        // 閉じた Dialog が panel に attach されても、フォーカスを奪わない（中の FocusScope は autoFocus = false）
        [UnityTest]
        public IEnumerator Attach_Closed_DoesNotMoveFocus()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);
            yield return FocusAndWait(f, f.Outside1);

            var dialog = AddDialog(f.Root);
            yield return null;

            Assert.IsFalse(dialog.isOpen);
            Assert.AreSame(f.Outside1, f.Focused);
        }

        // (A) Open() すると content 内の最初の Button にフォーカスが当たる
        [UnityTest]
        public IEnumerator Open_FocusesFirstButton()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);
            var dialog = AddDialog(f.Root);
            yield return FocusAndWait(f, f.Outside1);
            var opened = 0;
            dialog.opened += () => opened++;

            dialog.Open();
            yield return null;

            Assert.IsTrue(dialog.isOpen);
            Assert.AreEqual(1, opened);
            Assert.AreSame(CancelButton(dialog), f.Focused);
        }

        // (B) 開いている間、端の Button から外側へ動かしても content の中に留まる
        [UnityTest]
        public IEnumerator Open_MoveFromEdge_StaysInContent()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);
            var dialog = AddDialog(f.Root);
            yield return FocusAndWait(f, f.Outside2);
            dialog.Open();
            var cancel = CancelButton(dialog);
            var ok = OkButton(dialog);
            yield return WaitForLayout(cancel, ok);
            Assert.AreSame(cancel, f.Focused);

            // outside-1 / outside-2 は Dialog の上（左上）にある。既定のナビなら Up と Previous でそちらへ出る
            foreach (var direction in new[] { NavigationMoveEvent.Direction.Up, NavigationMoveEvent.Direction.Left, NavigationMoveEvent.Direction.Previous })
            {
                SendMove(cancel, direction);
                yield return null;
                Assert.AreSame(cancel, f.Focused, $"{direction} from the first button must stay in the dialog");
            }

            yield return FocusAndWait(f, ok);
            foreach (var direction in new[] { NavigationMoveEvent.Direction.Down, NavigationMoveEvent.Direction.Right, NavigationMoveEvent.Direction.Next })
            {
                SendMove(ok, direction);
                yield return null;
                Assert.AreSame(ok, f.Focused, $"{direction} from the last button must stay in the dialog");
            }
        }

        // (C) Open() 前にフォーカスされていた要素へ Close() で戻る。
        // Dialog が attach された時点のフォーカス（outside-1）ではなく、Open() の時点のフォーカス（outside-2）に戻ること
        [UnityTest]
        public IEnumerator Close_RestoresFocusBeforeOpen()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);
            yield return FocusAndWait(f, f.Outside1);
            var dialog = AddDialog(f.Root);
            yield return null;
            yield return FocusAndWait(f, f.Outside2);

            dialog.Open();
            yield return null;
            Assert.AreSame(CancelButton(dialog), f.Focused);

            dialog.Close();
            yield return null;

            Assert.IsFalse(dialog.isOpen);
            Assert.AreSame(f.Outside2, f.Focused);
        }

        // (D) dismissOnCancel = true なら、root への Cancel で閉じて closed が発火する
        [UnityTest]
        public IEnumerator Cancel_DismissOnCancel_Closes()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);
            var dialog = AddDialog(f.Root);
            var closed = 0;
            dialog.closed += () => closed++;
            dialog.Open();
            yield return null;

            SendCancel(f.Root);
            yield return null;

            Assert.IsFalse(dialog.isOpen);
            Assert.AreEqual(1, closed);
        }

        // (E) dismissOnCancel = false なら Cancel で閉じない
        [UnityTest]
        public IEnumerator Cancel_NoDismissOnCancel_StaysOpen()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);
            var dialog = AddDialog(f.Root);
            dialog.dismissOnCancel = false;
            var closed = 0;
            dialog.closed += () => closed++;
            dialog.Open();
            yield return null;

            SendCancel(f.Root);
            yield return null;

            Assert.IsTrue(dialog.isOpen);
            Assert.AreEqual(0, closed);
        }

        // (F) modal = true なら overlay を押すと閉じる。modal = false なら閉じない
        [UnityTest]
        public IEnumerator OverlayPointerDown_ClosesOnlyWhenModal()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);
            var dialog = AddDialog(f.Root);
            var overlay = dialog.Q(className: "hone-dialog__overlay");
            Assert.IsNotNull(overlay, "no element with class 'hone-dialog__overlay'");
            var closed = 0;
            dialog.closed += () => closed++;

            dialog.modal = false;
            dialog.Open();
            yield return WaitForLayout(overlay);
            Click(overlay);
            yield return null;
            Assert.IsTrue(dialog.isOpen, "a non-modal dialog must not close on the overlay");
            Assert.AreEqual(0, closed);

            dialog.modal = true;
            Click(overlay);
            yield return null;
            Assert.IsFalse(dialog.isOpen, "a modal dialog must close on the overlay");
            Assert.AreEqual(1, closed);
        }

        // (G) 2 つ重ねて開き、Cancel を 1 回送ると上（後に開いた方）だけが閉じる
        [UnityTest]
        public IEnumerator Cancel_TwoOpen_ClosesOnlyTopmost()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);
            var lower = AddDialog(f.Root);
            var upper = AddDialog(f.Root);
            var lowerClosed = 0;
            var upperClosed = 0;
            lower.closed += () => lowerClosed++;
            upper.closed += () => upperClosed++;
            lower.Open();
            yield return null;
            upper.Open();
            yield return null;

            SendCancel(f.Root);
            yield return null;

            Assert.IsFalse(upper.isOpen);
            Assert.AreEqual(1, upperClosed);
            Assert.IsTrue(lower.isOpen);
            Assert.AreEqual(0, lowerClosed);
        }

        // (H) 開いたまま panel から外しても例外が出ず、BackStack に残らない
        [UnityTest]
        public IEnumerator Detach_WhileOpen_RemovesFromBackStack()
        {
            Fixture f = null;
            yield return BuildFixture(x => f = x);
            var dialog = AddDialog(f.Root);
            dialog.Open();
            yield return null;
            var panel = f.Panel;

            Assert.DoesNotThrow(() => dialog.RemoveFromHierarchy());
            yield return null;

            // HandleCancel は積まれたものが無ければ false を返す
            Assert.IsFalse(BackStack.For(panel).HandleCancel(), "the detached dialog must not remain in the BackStack");
        }
    }
}
#endif
