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
    // block は振る舞いを持たないので、UXML が読み込めて各プリミティブが見つかることと、Gallery の配線（Destructive の行の Button で Dialog が開閉する）だけを確かめる。
    // Dialog の開閉とフォーカスの復元そのものは DialogTests が担う。ここでは Gallery の配線を通して、閉じたあとに元の Button にフォーカスが戻ることを 1 つ確かめる
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

        // (A) UXML が読み込め、行の型ごとのプリミティブが見つかる。Dialog は block の最後の子（親の全面を覆うため）
        [Test]
        public void Uxml_HasEachPrimitive()
        {
            var screen = Instantiate();

            Assert.IsNotNull(screen.Q<Switch>("notifications"));
            Assert.IsNotNull(screen.Q<Cycle>("quality"));
            Assert.IsNotNull(screen.Q<ToggleGroup>("density"));
            Assert.AreEqual(Hone.Button.Variant.Secondary, screen.Q<Hone.Button>("refresh").variant);
            Assert.AreEqual(Hone.Button.Variant.Destructive, screen.Q<Hone.Button>("delete").variant);
            var dialog = screen.Q<Dialog>("confirm-delete");
            Assert.IsNotNull(dialog);
            Assert.IsNotNull(dialog.Q<Hone.Button>("cancel"));
            Assert.IsNotNull(dialog.Q<Hone.Button>("confirm"));
            var block = screen.Q(className: "hone-settings-screen");
            Assert.AreSame(dialog, block.hierarchy.ElementAt(block.hierarchy.childCount - 1));
            Assert.AreEqual(5, screen.Query(className: "hone-settings-screen__row").ToList().Count);
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

        // (B) Destructive の行の Button で Dialog が開き、Cancel で閉じるとその Button にフォーカスが戻る。確定でも閉じる
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
    }
}
#endif
