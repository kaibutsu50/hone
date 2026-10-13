#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Hone.Sandbox.Gallery;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Tests
{
    // Editor で実行する前提（Gallery.unity を AssetDatabase のパスで読む）。
    // 実物のシーンを additive で読み込み、Screen Space の PanelRenderer の GalleryController が組んだメニューと言語の Cycle を操作する（シーンの配線ごと確かめる）。
    // 入力は SendEvent で送る（入力経路は通らない）。
    public class GalleryTests
    {
        const string ScenePath = "Assets/Hone/Sandbox/Gallery/Gallery.unity";
        const string PanelRendererName = "PanelRenderer";
        const string CounterName = "Gallery.Test.Counter";
        const int MaxWaitFrames = 60;
        // GalleryFocus の DelayMilliseconds（200）の 2 倍（当て直しと確認）より長く待つ
        const float FocusWaitSeconds = 0.6f;
        // 1 回の Build で factory が呼ばれる回数（en と選んだ言語の 2 行 × 短文と長文）
        const int CallsPerBuild = 4;

        Scene m_Scene;
        int m_CounterCalls;

        // メニューは UI が読み込まれた時点の登録内容で作るので、シーンを読み込む前に登録する
        [SetUp]
        public void SetUp()
        {
            m_CounterCalls = 0;
            GalleryController.Register(CounterName, text =>
            {
                m_CounterCalls++;
                return new Label(text);
            });
        }

        // Register は static で、Domain Reload 無効の Editor では Play をまたいで残る。Gallery の撮影に混ざらないよう必ず消す
        [UnityEngine.TestTools.UnityTearDown]
        public IEnumerator TearDown()
        {
            GalleryController.Unregister(CounterName);
            if (m_Scene.IsValid() && m_Scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(m_Scene);
        }

        GameObject PanelRendererObject() => m_Scene.GetRootGameObjects().First(go => go.name == PanelRendererName);

        IEnumerator LoadScene(Action<GalleryController> onReady)
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            m_Scene = SceneManager.GetSceneByPath(ScenePath);
            // 撮影と同じく片方の panel だけを有効にする。両方有効だと、読み込み時の確認で「did not get focus」の LogError が出た（読み込み時の確認が走るまで待つテストだけが落ちた。6000.7.0b4）。
            // 2 つの GalleryFocus が別々の panel にフォーカスを当て、片方が外れるためと見ているが、確かめていない
            m_Scene.GetRootGameObjects().First(go => go.name == "PanelRendererWorldSpace").SetActive(false);
            var controller = PanelRendererObject().GetComponent<GalleryController>();
            Assert.IsNotNull(controller, "GalleryController was not found on PanelRenderer");
            yield return WaitForPreview(controller);
            onReady(controller);
        }

        static IEnumerator WaitForPreview(GalleryController controller)
        {
            var frames = 0;
            while ((controller.preview == null || controller.preview.panel == null) && frames < MaxWaitFrames)
            {
                frames++;
                yield return null;
            }
            Assert.IsNotNull(controller.preview?.panel, $"preview was not built within {MaxWaitFrames} frames");
        }

        static VisualElement Root(GalleryController controller) => controller.preview.panel.visualTree;

        static ToggleGroup Components(GalleryController controller) => Root(controller).Q<ToggleGroup>("components");

        static Cycle Language(GalleryController controller) => Root(controller).Q<Cycle>("language");

        static Label ComponentName(GalleryController controller) => Root(controller).Q<Label>("component-name");

        static Hone.Button[] MenuItems(GalleryController controller) => Components(controller).Query<Hone.Button>().ToList().ToArray();

        static VisualElement Column(GalleryController controller, string name) =>
            controller.preview.contentContainer.Q(className: GalleryController.ColumnClass(name));

        static string[] Tags(GalleryController controller) =>
            controller.preview.contentContainer.Query<Label>(className: "gallery-tag").ToList().Select(l => l.text).ToArray();

        static Focusable Focused(GalleryController controller) => controller.preview.panel.focusController.focusedElement;

        static void Submit(VisualElement target)
        {
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        // (A)
        [UnityEngine.TestTools.UnityTest]
        public IEnumerator Load_MenuListsNamesInOrder_SelectsFirst()
        {
            GalleryController controller = null;
            yield return LoadScene(c => controller = c);
            var names = GalleryController.Names;

            CollectionAssert.AreEqual(names, MenuItems(controller).Select(b => b.text).ToList());
            var state = Components(controller).value;
            for (var i = 0; i < names.Count; i++)
                Assert.AreEqual(i == 0, state[i], $"item {i}");
            Assert.AreEqual(names[0], ComponentName(controller).text);
            Assert.IsNotNull(Column(controller, names[0]));
        }

        // (B)
        [UnityEngine.TestTools.UnityTest]
        public IEnumerator SubmitThirdMenuItem_SwitchesComponent_KeepsLanguage()
        {
            GalleryController controller = null;
            yield return LoadScene(c => controller = c);
            var names = GalleryController.Names;
            var language = Language(controller).value;

            Submit(MenuItems(controller)[2]);

            Assert.AreEqual(names[2], ComponentName(controller).text);
            Assert.IsNotNull(Column(controller, names[2]));
            Assert.IsNull(Column(controller, names[0]));
            Assert.AreEqual(language, Language(controller).value);
        }

        // (C)
        [UnityEngine.TestTools.UnityTest]
        public IEnumerator Language_ChoicesFollowTestStrings_SelectScriptSwitchesSecondRow()
        {
            GalleryController controller = null;
            yield return LoadScene(c => controller = c);
            var path = Path.Combine(Application.dataPath, "Hone/Sandbox/Gallery/TestStrings.json");
            var expected = GalleryController.ParseTestStrings(File.ReadAllText(path))
                .Where(s => s.Script != GalleryController.BaseScript).Select(s => s.Name).ToList();
            var componentName = ComponentName(controller).text;

            CollectionAssert.AreEqual(expected, Language(controller).choices);
            Assert.AreEqual(0, Language(controller).value);

            controller.SelectScript("ko");

            CollectionAssert.AreEqual(new[] { "en", "ko" }, Tags(controller));
            Assert.AreEqual(componentName, ComponentName(controller).text);
        }

        // (D) 読み込み時の当て直しが終わってから、Cycle にフォーカスを置いて言語を切り替える（選んでいるのは Dialog 以外の Hone.Button）
        [UnityEngine.TestTools.UnityTest]
        public IEnumerator SelectScript_WithCycleFocused_FocusStaysOnCycle()
        {
            GalleryController controller = null;
            yield return LoadScene(c => controller = c);
            yield return new WaitForSecondsRealtime(FocusWaitSeconds);
            var language = Language(controller);
            language.Focus();
            yield return null;
            Assert.AreSame(language, Focused(controller), "precondition: the Cycle has focus");

            controller.SelectScript("ko");
            yield return new WaitForSecondsRealtime(FocusWaitSeconds);

            Assert.AreSame(language, Focused(controller));
        }

        // (E)
        [UnityEngine.TestTools.UnityTest]
        public IEnumerator RefocusLater_FocusesFirstFocusableInPreview()
        {
            GalleryController controller = null;
            yield return LoadScene(c => controller = c);
            yield return new WaitForSecondsRealtime(FocusWaitSeconds);
            Language(controller).Focus();
            yield return null;

            controller.GetComponent<GalleryFocus>().RefocusLater();
            yield return new WaitForSecondsRealtime(FocusWaitSeconds);

            var expected = controller.preview.contentContainer.Q(className: "hone-focusable");
            Assert.IsNotNull(expected, "precondition: the preview has a .hone-focusable");
            Assert.AreSame(expected, Focused(controller));
        }

        // (F)
        [UnityEngine.TestTools.UnityTest]
        public IEnumerator SelectComponent_AlreadySelected_DoesNotBuild()
        {
            GalleryController controller = null;
            yield return LoadScene(c => controller = c);
            controller.SelectComponent(CounterName);
            Assert.AreEqual(CallsPerBuild, m_CounterCalls, "precondition: the counter was built once");

            controller.SelectComponent(CounterName);

            Assert.AreEqual(CallsPerBuild, m_CounterCalls);
        }

        // (G) 再読み込みでメニューは Names[0] の選択に戻り、右も Names[0] で組み直される。それを待ってから数える
        [UnityEngine.TestTools.UnityTest]
        public IEnumerator Reload_MenuNotDuplicated_SwitchBuildsOnce()
        {
            GalleryController controller = null;
            yield return LoadScene(c => controller = c);
            var names = GalleryController.Names;
            controller.SelectComponent(names[1]);
            var go = PanelRendererObject();

            go.SetActive(false);
            yield return null;
            go.SetActive(true);
            var frames = 0;
            while ((controller.preview?.panel == null || Column(controller, names[0]) == null) && frames < MaxWaitFrames)
            {
                frames++;
                yield return null;
            }
            Assert.IsNotNull(Column(controller, names[0]), $"the UI was not reloaded within {MaxWaitFrames} frames");

            Assert.AreEqual(names.Count, MenuItems(controller).Length);
            m_CounterCalls = 0;
            controller.SelectComponent(CounterName);
            Assert.AreEqual(CallsPerBuild, m_CounterCalls);
        }
    }
}
#endif
