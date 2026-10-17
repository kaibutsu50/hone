#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hone.Sandbox.Gallery;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hone.Sandbox.Tests
{
    // Editor で実行する前提（UXML と PanelSettings を AssetDatabase から読む）。
    // block は振る舞いを持たないので、確かめるのは次の 4 つ: UXML が読み込めて各プリミティブが見つかること、Gallery が文字列を差し込んで 4 つ並べること、
    // Gallery の配線（pause の開閉、quit で重ねて開く確認）とフォーカスの位置、block の USS が theme を通して当たっていること。
    // Dialog の trap・復元・Cancel そのものは DialogTests が担う。ここでは 2 つの Dialog が兄弟のときに、pause 側の trap が確認を開いている間のフォーカスを奪い返さないことと、
    // 確認を閉じたあとフォーカスが quit に戻って pause が開いたままであることを、Gallery の配線を通して確かめる。
    // コメントの (A)〜(D) は Issue #127 の受け入れ条件の番号。(A) の registry.json は cli/test/add.test.ts が確かめる
    public class PauseMenuTests
    {
        const string UxmlPath = "Assets/Hone/Blocks/PauseMenu/PauseMenu.uxml";
        const string PanelSettingsPath = "Assets/Hone/Sandbox/PanelSettings.asset";
        const string SandboxUxmlPath = "Assets/Hone/Sandbox/Sandbox.uxml";
        const string EntryName = "Hone.PauseMenu";
        const int MaxWaitFrames = 60;

        readonly List<Object> m_Created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in m_Created)
                Object.Destroy(obj);
            m_Created.Clear();
        }

        static VisualElement Instantiate()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(tree, $"{UxmlPath} was not found");
            return tree.Instantiate();
        }

        static IReadOnlyList<ScriptStrings> LoadTestStrings()
        {
            var path = Path.Combine(Application.dataPath, "Hone/Sandbox/Gallery/TestStrings.json");
            return GalleryController.ParseTestStrings(File.ReadAllText(path));
        }

        // PanelRenderer を 1 つ作り、UI が読み込まれたら root を返す。PanelSettings は複製して、panel とフォーカスと BackStack の状態をテスト間で共有しない
        IEnumerator CreatePanel(System.Action<VisualElement> onReady)
        {
            var source = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            Assert.IsNotNull(source, $"{PanelSettingsPath} was not found");
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SandboxUxmlPath);
            Assert.IsNotNull(uxml, $"{SandboxUxmlPath} was not found");
            var panelSettings = Object.Instantiate(source);
            m_Created.Add(panelSettings);
            var go = new GameObject("PauseMenuTests");
            m_Created.Add(go);
            go.SetActive(false);
            var panelRenderer = go.AddComponent<PanelRenderer>();
            panelRenderer.panelSettings = panelSettings;
            panelRenderer.visualTreeAsset = uxml;
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

        // Gallery が並べる 4 つのうち 1 つだけを panel に足す。attach の時点で pause が開くので、複数あると最後に開いたものがフォーカスと BackStack の先頭を取る。
        // container を panel に足す前に余分を外しておくと、外した分は attach されず、開かない
        static VisualElement AddOneMenu(VisualElement root)
        {
            var container = new VisualElement();
            GalleryController.Build(container, EntryName, GalleryController.Pick(LoadTestStrings(), "ja"));
            var menus = container.Query(className: "hone-pause-menu").ToList();
            Assert.AreEqual(4, menus.Count);
            foreach (var extra in menus.Skip(1))
                extra.parent.RemoveFromHierarchy();
            root.Add(container);
            return menus[0];
        }

        // panel を作り、menu を 1 つ足して 1 frame 進める。フォーカスの変更は非同期なので、直後に focusedElement を読まない
        IEnumerator OpenMenu(System.Action<VisualElement, VisualElement> onReady)
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var menu = AddOneMenu(root);
            yield return null;
            onReady(root, menu);
        }

        static void Submit(VisualElement target)
        {
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
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

        static Focusable Focused(VisualElement root) => root.panel.focusController.focusedElement;

        static IEnumerator WaitForLayout(VisualElement element)
        {
            var frames = 0;
            while (frames < MaxWaitFrames && !(element.worldBound.height > 0))
            {
                frames++;
                yield return null;
            }
            Assert.Greater(element.worldBound.height, 0f, "the element was not laid out");
        }

        // UXML が読み込め、プリミティブが見つかる。2 つの Dialog は block の子の兄弟で、確認が pause より後ろ（上に描かれ、入力も受ける）
        [Test]
        public void Uxml_HasEachPrimitiveAndConfirmIsLaterSibling()
        {
            var screen = Instantiate();

            var pause = screen.Q<Dialog>("pause");
            var confirm = screen.Q<Dialog>("confirm-quit");
            Assert.IsNotNull(pause);
            Assert.IsNotNull(confirm);
            Assert.AreEqual(Hone.Button.Variant.Default, pause.Q<Hone.Button>("resume").variant);
            Assert.AreEqual(Hone.Button.Variant.Outline, pause.Q<Hone.Button>("settings").variant);
            Assert.AreEqual(Hone.Button.Variant.Outline, pause.Q<Hone.Button>("quit").variant);
            Assert.AreEqual(Hone.Button.Variant.Outline, confirm.Q<Hone.Button>("cancel").variant);
            Assert.AreEqual(Hone.Button.Variant.Destructive, confirm.Q<Hone.Button>("confirm").variant);
            var block = screen.Q(className: "hone-pause-menu");
            Assert.AreSame(block, pause.hierarchy.parent);
            Assert.AreSame(block, confirm.hierarchy.parent);
            Assert.Greater(block.IndexOf(confirm), block.IndexOf(pause));
            Assert.IsFalse(pause.isOpen);
            Assert.IsFalse(confirm.isOpen);
        }

        // Gallery に登録されていて、英語と選んだ 1 言語の短文と長文で 4 つ並ぶ。pause のタイトルと確認の本文にはその文字列が入る
        [Test]
        public void Gallery_BuildsOneMenuPerScriptAndKind()
        {
            CollectionAssert.Contains(GalleryController.Names, EntryName);
            var strings = GalleryController.Pick(LoadTestStrings(), "de-long");
            var container = new VisualElement();

            GalleryController.Build(container, EntryName, strings);

            var menus = container.Query(className: "hone-pause-menu").ToList();
            Assert.AreEqual(4, menus.Count);
            var expected = strings.SelectMany(s => new[] { s.Short, s.Long }).ToList();
            CollectionAssert.AreEqual(expected, menus.Select(m => m.Q<Dialog>("pause").Q<Label>(className: "hone-dialog__title").text).ToList());
            CollectionAssert.AreEqual(expected, menus.Select(m => m.Q<Dialog>("confirm-quit").Q<Label>(className: "hone-dialog__description").text).ToList());
        }

        // (B) pause は開いた状態で並び、resume にフォーカスが当たる。Cancel で閉じる
        [UnityTest]
        public IEnumerator Gallery_PauseOpensWithFocusOnResumeAndCancelCloses()
        {
            VisualElement root = null, menu = null;
            yield return OpenMenu((r, m) => { root = r; menu = m; });
            var pause = menu.Q<Dialog>("pause");
            var confirm = menu.Q<Dialog>("confirm-quit");

            Assert.IsTrue(pause.isOpen);
            Assert.IsFalse(confirm.isOpen);
            Assert.AreSame(pause.Q<Hone.Button>("resume"), Focused(root));

            SendCancel((VisualElement)Focused(root));
            yield return null;

            Assert.IsFalse(pause.isOpen);
        }

        // (B) resume で pause が閉じる
        [UnityTest]
        public IEnumerator Gallery_ResumeClosesPause()
        {
            VisualElement root = null, menu = null;
            yield return OpenMenu((r, m) => { root = r; menu = m; });
            var pause = menu.Q<Dialog>("pause");

            Submit(pause.Q<Hone.Button>("resume"));
            yield return null;

            Assert.IsFalse(pause.isOpen);
        }

        // (C) 方向入力で quit に移って確認を開くと、pause は開いたまま確認が重なり、フォーカスは確認の中の最初の Button（cancel）に移る。
        // Cancel で閉じると確認だけが閉じて、フォーカスは quit に戻る。cancel の Button で閉じても同じ。もう一度 Cancel を送ると pause が閉じる
        [UnityTest]
        public IEnumerator Gallery_QuitOpensConfirmAndCloseReturnsFocusToQuit()
        {
            VisualElement root = null, menu = null;
            yield return OpenMenu((r, m) => { root = r; menu = m; });
            var pause = menu.Q<Dialog>("pause");
            var confirm = menu.Q<Dialog>("confirm-quit");
            var resume = pause.Q<Hone.Button>("resume");
            var settings = pause.Q<Hone.Button>("settings");
            var quit = pause.Q<Hone.Button>("quit");
            var cancel = confirm.Q<Hone.Button>("cancel");
            yield return WaitForLayout(quit);

            SendMove(resume, NavigationMoveEvent.Direction.Down);
            yield return null;
            Assert.AreSame(settings, Focused(root));
            SendMove(settings, NavigationMoveEvent.Direction.Down);
            yield return null;
            Assert.AreSame(quit, Focused(root));

            Submit(quit);
            yield return null;
            Assert.IsTrue(confirm.isOpen);
            Assert.IsTrue(pause.isOpen);
            Assert.AreSame(cancel, Focused(root));

            SendCancel(cancel);
            yield return null;
            Assert.IsFalse(confirm.isOpen);
            Assert.IsTrue(pause.isOpen);
            Assert.AreSame(quit, Focused(root));

            Submit(quit);
            yield return null;
            Assert.IsTrue(confirm.isOpen);
            Submit(cancel);
            yield return null;
            Assert.IsFalse(confirm.isOpen);
            Assert.IsTrue(pause.isOpen);
            Assert.AreSame(quit, Focused(root));

            SendCancel(quit);
            yield return null;
            Assert.IsFalse(pause.isOpen);
        }

        // (D) 確認が開いている間、pause 側の trap がフォーカスを奪い返さず、方向入力と Tab で pause 側の Button へ移らない
        [UnityTest]
        public IEnumerator Gallery_ConfirmOpen_FocusStaysInConfirm()
        {
            VisualElement root = null, menu = null;
            yield return OpenMenu((r, m) => { root = r; menu = m; });
            var pause = menu.Q<Dialog>("pause");
            var confirm = menu.Q<Dialog>("confirm-quit");
            var quit = pause.Q<Hone.Button>("quit");
            quit.Focus();
            yield return WaitForLayout(quit);
            Submit(quit);
            for (var i = 0; i < 5; i++)
            {
                yield return null;
                Assert.IsTrue(confirm.Contains((VisualElement)Focused(root)), $"the focus left the confirm dialog {i + 1} frames after it opened");
            }

            var directions = new[]
            {
                NavigationMoveEvent.Direction.Up, NavigationMoveEvent.Direction.Down, NavigationMoveEvent.Direction.Left, NavigationMoveEvent.Direction.Right,
                NavigationMoveEvent.Direction.Previous, NavigationMoveEvent.Direction.Next,
            };
            foreach (var button in new[] { confirm.Q<Hone.Button>("cancel"), confirm.Q<Hone.Button>("confirm") })
            {
                foreach (var direction in directions)
                {
                    button.Focus();
                    yield return null;
                    SendMove(button, direction);
                    yield return null;
                    var focused = Focused(root) as VisualElement;
                    Assert.IsTrue(confirm.Contains(focused), $"{direction} from {button.name} moved the focus to {focused?.name ?? "nothing"}, outside the confirm dialog");
                }
            }
        }

        // block の USS が theme を通して当たっている（@import の抜けやセレクタの打ち間違いを捕まえる）。
        // pause は block の全面を覆い、項目は縦に並んで Button が項目の幅いっぱいに広がる。見た目そのものはスクリーンショットで確かめる
        [UnityTest]
        public IEnumerator Gallery_UssAppliesItemsStackAndStretch()
        {
            VisualElement root = null, menu = null;
            yield return OpenMenu((r, m) => { root = r; menu = m; });
            var pause = menu.Q<Dialog>("pause");
            var items = menu.Q(className: "hone-pause-menu__items");
            var buttons = new[] { "resume", "settings", "quit" }.Select(n => pause.Q<Hone.Button>(n)).ToList();
            yield return WaitForLayout(buttons[2]);

            Assert.AreEqual(FlexDirection.Column, items.resolvedStyle.flexDirection);
            Assert.AreEqual(menu.worldBound.width, pause.worldBound.width, 1f, "the pause dialog does not cover the block");
            Assert.AreEqual(menu.worldBound.height, pause.worldBound.height, 1f, "the pause dialog does not cover the block");
            Assert.Greater(items.worldBound.width, 0f);
            foreach (var button in buttons)
                Assert.AreEqual(items.worldBound.width, button.worldBound.width, 1f, $"{button.name} does not fill the items");
            Assert.Less(buttons[0].worldBound.yMax, buttons[1].worldBound.yMin);
            Assert.Less(buttons[1].worldBound.yMax, buttons[2].worldBound.yMin);
        }
    }
}
#endif
