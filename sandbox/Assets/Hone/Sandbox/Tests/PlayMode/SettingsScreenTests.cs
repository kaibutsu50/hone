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
    // Gallery の配線（Destructive の行の Button で Dialog が開閉する）、block の USS が theme を通して当たっていること。
    // Dialog の開閉とフォーカスの復元そのものは DialogTests が担う。ここでは Gallery の配線を通して、Cancel と確定のどちらで閉じても元の Button にフォーカスが戻ることを確かめる。
    // コメントの (A)(B) は Issue #113 の受け入れ条件の番号。(A) の registry.json は cli/test/add.test.ts が確かめる
    public class SettingsScreenTests
    {
        const string UxmlPath = "Assets/Hone/Blocks/SettingsScreen/SettingsScreen.uxml";
        const string PanelSettingsPath = "Assets/Hone/Sandbox/PanelSettings.asset";
        const string SandboxUxmlPath = "Assets/Hone/Sandbox/Sandbox.uxml";
        const string EntryName = "Hone.SettingsScreen";
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

        // PanelRenderer を 1 つ作り、UI が読み込まれたら root を返す。PanelSettings は複製して、panel とフォーカスの状態をテスト間で共有しない
        IEnumerator CreatePanel(System.Action<VisualElement> onReady)
        {
            var source = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            Assert.IsNotNull(source, $"{PanelSettingsPath} was not found");
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SandboxUxmlPath);
            Assert.IsNotNull(uxml, $"{SandboxUxmlPath} was not found");
            var panelSettings = Object.Instantiate(source);
            m_Created.Add(panelSettings);
            var go = new GameObject("SettingsScreenTests");
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

        static void Submit(VisualElement target)
        {
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        // UXML が読み込め、行の型ごとのプリミティブが見つかる。Dialog は block の最後の子（後ろの兄弟が overlay の上に描かれないため）
        [Test]
        public void Uxml_HasEachPrimitive()
        {
            var screen = Instantiate();

            Assert.IsNotNull(screen.Q<Switch>("notifications"));
            Assert.IsNotNull(screen.Q<Cycle>("quality"));
            Assert.IsNotNull(screen.Q<ToggleGroup>("density"));
            var refresh = screen.Q<Hone.Button>("refresh");
            Assert.IsNotNull(refresh);
            Assert.AreEqual(Hone.Button.Variant.Secondary, refresh.variant);
            var delete = screen.Q<Hone.Button>("delete");
            Assert.IsNotNull(delete);
            Assert.AreEqual(Hone.Button.Variant.Destructive, delete.variant);
            var dialog = screen.Q<Dialog>("confirm-delete");
            Assert.IsNotNull(dialog);
            Assert.IsNotNull(dialog.Q<Hone.Button>("cancel"));
            Assert.IsNotNull(dialog.Q<Hone.Button>("confirm"));
            var block = screen.Q(className: "hone-settings-screen");
            Assert.AreSame(dialog, block.hierarchy.ElementAt(block.hierarchy.childCount - 1));
        }

        // (B) Gallery に登録されていて、英語と選んだ 1 言語の短文と長文で 4 つ並ぶ。各 block の見出しと説明にはその文字列が入る
        [Test]
        public void Gallery_BuildsOneScreenPerScriptAndKind()
        {
            CollectionAssert.Contains(GalleryController.Names, EntryName);
            var strings = GalleryController.Pick(LoadTestStrings(), "de-long");
            var container = new VisualElement();

            GalleryController.Build(container, EntryName, strings);

            var screens = container.Query(className: "hone-settings-screen").ToList();
            Assert.AreEqual(4, screens.Count);
            var expected = strings.SelectMany(s => new[] { s.Short, s.Long }).ToList();
            CollectionAssert.AreEqual(expected, screens.Select(s => s.Q<Label>(className: "hone-settings-screen__heading").text).ToList());
            CollectionAssert.AreEqual(expected, screens.Select(s => s.Q<Label>(className: "hone-settings-screen__description").text).ToList());
        }

        // (B) Destructive の行の Button で Dialog が開き、Cancel で閉じるとその Button にフォーカスが戻る。確定でも閉じ、同じくフォーカスが戻る
        [UnityTest]
        public IEnumerator Gallery_DeleteButtonOpensDialogAndCloseRestoresFocus()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var container = new VisualElement();
            root.Add(container);
            GalleryController.Build(container, EntryName, GalleryController.Pick(LoadTestStrings(), "ja"));
            yield return null;
            var screen = container.Q(className: "hone-settings-screen");
            var dialog = screen.Q<Dialog>("confirm-delete");
            var delete = screen.Q<Hone.Button>("delete");
            delete.Focus();
            yield return null;
            Assert.AreSame(delete, root.panel.focusController.focusedElement);
            Assert.IsFalse(dialog.isOpen);

            Submit(delete);
            yield return null;

            Assert.IsTrue(dialog.isOpen);
            Submit(dialog.Q<Hone.Button>("cancel"));
            yield return null;
            Assert.IsFalse(dialog.isOpen);
            Assert.AreSame(delete, root.panel.focusController.focusedElement);

            Submit(delete);
            yield return null;
            Assert.IsTrue(dialog.isOpen);
            Submit(dialog.Q<Hone.Button>("confirm"));
            yield return null;
            Assert.IsFalse(dialog.isOpen);
            Assert.AreSame(delete, root.panel.focusController.focusedElement);
        }

        // (C) block の USS が theme を通して当たっている（@import の抜けやセレクタの打ち間違いで、説明が折り返さず操作がはみ出す退行を捕まえる）。
        // 幅 400 の容器に、長い 1 語（de-long）の長文で並べる。説明は折り返す設定になり、行の右の操作は行の外へはみ出さない。
        // 見た目そのもの（折り返しの位置、省略記号）はスクリーンショットで確かめる
        [UnityTest]
        public IEnumerator Gallery_UssAppliesDescriptionWrapsAndControlsStayInsideRow()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var container = new VisualElement();
            container.style.width = 400;
            root.Add(container);
            GalleryController.Build(container, EntryName, GalleryController.Pick(LoadTestStrings(), "de-long"));
            var rows = container.Query(className: "hone-settings-screen__row").ToList();
            Assert.AreEqual(4 * 5, rows.Count);
            var frames = 0;
            while (frames < MaxWaitFrames && rows.Exists(r => !(r.worldBound.height > 0)))
            {
                frames++;
                yield return null;
            }

            foreach (var description in container.Query<Label>(className: "hone-settings-screen__description").ToList())
                Assert.AreEqual(WhiteSpace.Normal, description.resolvedStyle.whiteSpace);
            foreach (var row in rows)
            {
                Assert.Greater(row.worldBound.height, 0f, "the row was not laid out");
                var control = row.Q(className: "hone-settings-screen__control");
                Assert.IsNotNull(control);
                Assert.LessOrEqual(control.worldBound.xMax, row.worldBound.xMax + 1f, $"{control.GetType().Name} overflows the row");
            }
        }
    }
}
#endif
