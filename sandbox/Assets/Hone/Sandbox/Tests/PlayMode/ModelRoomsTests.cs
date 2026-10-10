#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Hone.Sandbox.ModelRooms;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Tests
{
    // Editor で実行する前提（ModelRooms.unity を AssetDatabase のパスで読む）。
    // 実物のシーンを additive で読み込み、ModelRoomsController が組んだ 3 列を操作する（シーンの配線ごと確かめる）。
    // 入力は SendEvent で送る（入力経路は通らない）。
    public class ModelRoomsTests
    {
        const string ScenePath = "Assets/Hone/Sandbox/ModelRooms/ModelRooms.unity";
        const string RoomA = "room-test-a";
        const string RoomB = "room-test-b";
        const int MaxWaitFrames = 60;

        // Register は static で、Domain Reload 無効の Editor では Play をまたいで残る。他のテストや撮影に混ざらないよう、登録した className を必ず消す
        readonly List<string> m_Registered = new List<string>();
        Scene m_Scene;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var className in m_Registered)
                ModelRoomsController.Unregister(className);
            m_Registered.Clear();
            if (m_Scene.IsValid() && m_Scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(m_Scene);
        }

        void Register(ModelRoom room)
        {
            m_Registered.Add(room.className);
            ModelRoomsController.Register(room);
        }

        // ルームの登録は、シーンを読み込む前に済ませる（dropdown は UI が読み込まれた時点の登録内容で作る）
        IEnumerator LoadScene(System.Action<ModelRoomsController> onReady)
        {
            var op = EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return op;
            m_Scene = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);
            var controller = Object.FindAnyObjectByType<ModelRoomsController>();
            Assert.IsNotNull(controller, "ModelRoomsController was not found in the scene");

            var frames = 0;
            while ((controller.columns == null || controller.columns.panel == null || controller.columns.childCount == 0) && frames < MaxWaitFrames)
            {
                frames++;
                yield return null;
            }
            Assert.IsNotNull(controller.columns?.panel, $"columns were not built within {MaxWaitFrames} frames");
            onReady(controller);
        }

        static List<VisualElement> Columns(ModelRoomsController controller) =>
            controller.columns.Query(className: "model-room-column").ToList();

        static VisualElement StageOf(VisualElement column) => column.Q(className: "model-room-stage");

        static MessageWindow MessageOf(VisualElement column) => column.Q<MessageWindow>();

        static Dialog DialogOf(VisualElement column) => column.Q<Dialog>();

        static Label StatusOf(VisualElement column) => column.Q<Label>(className: "model-room-status");

        static void Submit(VisualElement target)
        {
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        // 最後のページで completed が出るまで Advance する。ページ数より多く回しても出ないなら失敗
        static void AdvanceToCompleted(MessageWindow window)
        {
            var completed = false;
            window.completed += () => completed = true;
            for (var i = 0; i < window.pageCount + 1 && !completed; i++)
                window.Advance();
            Assert.IsTrue(completed, "completed was not raised");
        }

        // (A)
        [UnityTest]
        public IEnumerator Build_ThreeColumns_EachHasMessageWindowAndDialog()
        {
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);

            var columns = Columns(controller);
            Assert.AreEqual(3, columns.Count);
            CollectionAssert.AreEqual(
                new[] { "素", "USS だけ", "注入あり" },
                columns.Select(c => c.Q(className: "model-room-column__header").Q<Label>().text).ToList());
            foreach (var column in columns)
            {
                Assert.AreEqual(1, column.Query<MessageWindow>().ToList().Count);
                Assert.AreEqual(1, column.Query<Dialog>().ToList().Count);
            }
        }

        // (B) 2 つ登録し、後ろのほうを SelectRoom で選ぶ。最初に組まれる先頭のルームではなく、選び直した結果を見る
        [UnityTest]
        public IEnumerator SelectRoom_AddsClassToColumns2And3Only_InjectsColumn3Once()
        {
            var injectedA = new List<VisualElement>();
            var injectedB = new List<VisualElement>();
            Register(new ModelRoom { displayName = "A", className = RoomA, inject = injectedA.Add });
            Register(new ModelRoom { displayName = "B", className = RoomB, inject = injectedB.Add });
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);
            Assert.AreEqual(0, injectedB.Count, "inject of an unselected room was called");

            controller.SelectRoom(RoomB);

            var stages = Columns(controller).Select(StageOf).ToList();
            Assert.IsFalse(stages[0].ClassListContains(RoomB));
            Assert.IsTrue(stages[1].ClassListContains(RoomB));
            Assert.IsTrue(stages[2].ClassListContains(RoomB));
            Assert.IsFalse(stages[1].ClassListContains(RoomA));
            Assert.IsFalse(stages[2].ClassListContains(RoomA));
            Assert.AreEqual(1, injectedB.Count);
            Assert.AreSame(stages[2], injectedB[0]);
        }

        // (C)
        [UnityTest]
        public IEnumerator Advance_ToCompleted_OpensColumnDialog()
        {
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);
            var column = Columns(controller)[0];
            Assert.IsFalse(DialogOf(column).isOpen);

            AdvanceToCompleted(MessageOf(column));

            Assert.IsTrue(DialogOf(column).isOpen);
        }

        // (D)
        [UnityTest]
        public IEnumerator YesSubmit_ClosesDialog_StatusShowsYes()
        {
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);
            var column = Columns(controller)[0];
            AdvanceToCompleted(MessageOf(column));

            Submit(column.Q<Hone.Button>("yes"));

            Assert.IsFalse(DialogOf(column).isOpen);
            Assert.AreEqual("はい", StatusOf(column).text);
        }

        // (E)
        [UnityTest]
        public IEnumerator AgainSubmit_ResetsPageIndexToZero()
        {
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);
            var column = Columns(controller)[0];
            AdvanceToCompleted(MessageOf(column));
            Assert.AreNotEqual(0, MessageOf(column).pageIndex);

            Submit(column.Q<UnityEngine.UIElements.Button>("again"));

            Assert.AreEqual(0, MessageOf(column).pageIndex);
        }
    }
}
#endif
