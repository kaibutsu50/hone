#if UNITY_EDITOR
using System;
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

        const string RoomFontPath = "Assets/Hone/Sandbox/Fonts/MPLUSRounded1c.asset";
        const string TextAdventureFontPath = "Assets/Hone/Sandbox/Fonts/DotGothic16-28.asset";

        // Register は static で、Domain Reload 無効の Editor では Play をまたいで残る。他のテストや撮影に混ざらないよう、登録した className を必ず消す
        readonly List<string> m_Registered = new List<string>();
        Scene m_Scene;

        // inject が呼ばれた時点の舞台の状態
        struct InjectRecord
        {
            public VisualElement Stage;
            public bool Attached;
            public int PageIndex;
        }

        // クラシック JRPG 風とテキストアドベンチャー風は BeforeSceneLoad で登録される。基盤のテストは「登録したルームだけがある」前提なので、外してから始め、終わったら登録を戻す
        // （戻すと末尾に付くので、ルームが 2 つ以上あると登録の順は戻らない）。ルームのテストは、自分で Register する。
        // BeforeSceneLoad で登録するルームを足したら、ここで同じように外し、TearDown で戻す
        [SetUp]
        public void SetUp()
        {
            ModelRoomsController.Unregister(ClassicJrpgRoom.ClassName);
            ModelRoomsController.Unregister(TextAdventureRoom.ClassName);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var className in m_Registered)
                ModelRoomsController.Unregister(className);
            m_Registered.Clear();
            ModelRoomsController.Register(ClassicJrpgRoom.Room);
            ModelRoomsController.Register(TextAdventureRoom.Room);
            if (m_Scene.IsValid() && m_Scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(m_Scene);
        }

        void Register(ModelRoom room)
        {
            m_Registered.Add(room.className);
            ModelRoomsController.Register(room);
        }

        static Action<VisualElement> Recorder(List<InjectRecord> records) => stage =>
            records.Add(new InjectRecord { Stage = stage, Attached = stage.panel != null, PageIndex = stage.Q<MessageWindow>().pageIndex });

        // ルームの登録は、シーンを読み込む前に済ませる（dropdown は UI が読み込まれた時点の登録内容で作る）
        IEnumerator LoadScene(Action<ModelRoomsController> onReady)
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            m_Scene = SceneManager.GetSceneByPath(ScenePath);
            var controller = m_Scene.GetRootGameObjects()
                .Select(go => go.GetComponentInChildren<ModelRoomsController>())
                .FirstOrDefault(c => c != null);
            Assert.IsNotNull(controller, "ModelRoomsController was not found in the scene");

            var frames = 0;
            while ((controller.columns == null || controller.columns.panel == null) && frames < MaxWaitFrames)
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

        static DropdownField RoomDropdown(ModelRoomsController controller) => controller.columns.panel.visualTree.Q<DropdownField>("room");

        static void Submit(VisualElement target)
        {
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        // 最後のページで completed が出るまで Advance する。ページ数の 2 倍（文字送りがあるとページごとに 2 回要る）回しても出ないなら失敗
        static void AdvanceToCompleted(MessageWindow window)
        {
            var completed = false;
            window.completed += () => completed = true;
            for (var i = 0; i < window.pageCount * 2 && !completed; i++)
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

        // ルーム 0 件（BeforeSceneLoad で登録されるルームは SetUp で外してある）
        [UnityTest]
        public IEnumerator NoRoom_DropdownShowsPlaceholder_StagesHaveNoRoomClass()
        {
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);

            CollectionAssert.AreEqual(new[] { "デフォルト" }, RoomDropdown(controller).choices);
            foreach (var stage in Columns(controller).Select(StageOf))
                CollectionAssert.AreEqual(new[] { "model-room-stage" }, stage.GetClasses().ToList());
        }

        // (B) 2 つ登録し、後ろのほうを SelectRoom で選ぶ。最初に組まれる先頭のルームではなく、選び直した結果を見る
        [UnityTest]
        public IEnumerator SelectRoom_AddsClassToColumns2And3Only_InjectsColumn3Once()
        {
            var injectedA = new List<InjectRecord>();
            var injectedB = new List<InjectRecord>();
            Register(new ModelRoom { displayName = "A", className = RoomA, inject = Recorder(injectedA) });
            Register(new ModelRoom { displayName = "B", className = RoomB, inject = Recorder(injectedB) });
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);
            Assert.AreEqual(0, injectedB.Count, "inject of an unselected room was called");

            controller.SelectRoom(RoomB);

            var stages = Columns(controller).Select(StageOf).ToList();
            CollectionAssert.AreEqual(new[] { "model-room-stage" }, stages[0].GetClasses().ToList());
            Assert.IsTrue(stages[1].ClassListContains(RoomB));
            Assert.IsTrue(stages[2].ClassListContains(RoomB));
            Assert.IsFalse(stages[1].ClassListContains(RoomA));
            Assert.IsFalse(stages[2].ClassListContains(RoomA));
            Assert.AreEqual(1, injectedB.Count);
            Assert.AreSame(stages[2], injectedB[0].Stage);
            Assert.IsTrue(injectedB[0].Attached, "inject was called before the stage was attached");
        }

        // 最初の読み込みでも、inject は attach の後、メッセージの Show の前に呼ばれる（inject で MessageWindow を変えれば 1 ページ目から効く）
        [UnityTest]
        public IEnumerator FirstLoad_InjectRunsAfterAttachAndBeforeShow()
        {
            var injected = new List<InjectRecord>();
            Register(new ModelRoom { displayName = "A", className = RoomA, inject = Recorder(injected) });
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);

            Assert.AreEqual(1, injected.Count);
            Assert.AreSame(StageOf(Columns(controller)[2]), injected[0].Stage);
            Assert.IsTrue(injected[0].Attached, "inject was called before the stage was attached");
            Assert.AreEqual(-1, injected[0].PageIndex, "inject was called after Show");
            Assert.AreEqual(0, MessageOf(Columns(controller)[2]).pageIndex);
        }

        // 選び直すと 3 列とも新しい要素で組み直され、進めたメッセージと結果も初めに戻る
        [UnityTest]
        public IEnumerator SelectRoom_RebuildsAllColumns()
        {
            var injectedA = new List<InjectRecord>();
            Register(new ModelRoom { displayName = "A", className = RoomA, inject = Recorder(injectedA) });
            Register(new ModelRoom { displayName = "B", className = RoomB });
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);
            var before = Columns(controller);
            AdvanceToCompleted(MessageOf(before[0]));
            Submit(before[0].Q<Hone.Button>("yes"));

            controller.SelectRoom(RoomB);
            controller.SelectRoom(RoomA);

            var after = Columns(controller);
            Assert.AreEqual(3, after.Count);
            for (var i = 0; i < 3; i++)
                Assert.AreNotSame(before[i], after[i]);
            Assert.AreEqual(2, injectedA.Count);
            Assert.AreSame(StageOf(after[2]), injectedA[1].Stage);
            Assert.AreEqual(0, MessageOf(after[0]).pageIndex);
            Assert.AreEqual("", StatusOf(after[0]).text);
        }

        // 同じ className の再登録は置き換え（dropdown に 1 つだけ、新しい displayName で出る）
        [UnityTest]
        public IEnumerator Register_SameClassNameReplaces()
        {
            Register(new ModelRoom { displayName = "Old", className = RoomA });
            Register(new ModelRoom { displayName = "New", className = RoomA });
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);

            CollectionAssert.AreEqual(new[] { "New" }, RoomDropdown(controller).choices);
        }

        [TestCase(null)]
        [TestCase("")]
        public void Register_EmptyDisplayName_Throws(string displayName)
        {
            Assert.Throws<ArgumentException>(() =>
                ModelRoomsController.Register(new ModelRoom { displayName = displayName, className = RoomA }));
        }

        // inject の例外はログに出て、3 列とも組まれてメッセージが出る
        [UnityTest]
        public IEnumerator InjectThrows_OtherColumnsStillShowMessage()
        {
            Register(new ModelRoom { displayName = "A", className = RoomA, inject = stage => throw new InvalidOperationException("inject failed") });
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: inject failed");
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);

            var columns = Columns(controller);
            Assert.AreEqual(3, columns.Count);
            foreach (var column in columns)
                Assert.AreEqual(0, MessageOf(column).pageIndex);
        }

        // (C) 送り切った列の Dialog だけが開く
        [UnityTest]
        public IEnumerator Advance_ToCompleted_OpensColumnDialog([Values(0, 1, 2)] int index)
        {
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);
            var columns = Columns(controller);
            Assert.IsFalse(DialogOf(columns[index]).isOpen);

            AdvanceToCompleted(MessageOf(columns[index]));

            for (var i = 0; i < columns.Count; i++)
                Assert.AreEqual(i == index, DialogOf(columns[i]).isOpen, $"column {i}");
        }

        // (D) その列の見出しにだけ「はい」が出る
        [UnityTest]
        public IEnumerator YesSubmit_ClosesDialog_StatusShowsYes([Values(0, 1, 2)] int index)
        {
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);
            var columns = Columns(controller);
            AdvanceToCompleted(MessageOf(columns[index]));

            Submit(columns[index].Q<Hone.Button>("yes"));

            Assert.IsFalse(DialogOf(columns[index]).isOpen);
            for (var i = 0; i < columns.Count; i++)
                Assert.AreEqual(i == index ? "はい" : "", StatusOf(columns[i]).text, $"column {i}");
        }

        [UnityTest]
        public IEnumerator NoSubmit_ClosesDialog_StatusShowsNo([Values(0, 1, 2)] int index)
        {
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);
            var column = Columns(controller)[index];
            AdvanceToCompleted(MessageOf(column));

            Submit(column.Q<Hone.Button>("no"));

            Assert.IsFalse(DialogOf(column).isOpen);
            Assert.AreEqual("いいえ", StatusOf(column).text);
        }

        // (E) 開いていた Dialog が閉じ、メッセージが 1 ページ目に戻る
        [UnityTest]
        public IEnumerator AgainSubmit_ResetsPageIndexToZero([Values(0, 1, 2)] int index)
        {
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);
            var column = Columns(controller)[index];
            AdvanceToCompleted(MessageOf(column));
            Assert.AreNotEqual(0, MessageOf(column).pageIndex);

            Submit(column.Q<UnityEngine.UIElements.Button>("again"));

            Assert.AreEqual(0, MessageOf(column).pageIndex);
            Assert.IsFalse(DialogOf(column).isOpen);
        }

        // 「もう一度」の後に最後まで送ると、Dialog がもう一度開く
        [UnityTest]
        public IEnumerator AgainSubmit_ThenAdvanceToCompleted_OpensDialogAgain()
        {
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);
            var column = Columns(controller)[0];
            AdvanceToCompleted(MessageOf(column));
            Submit(column.Q<Hone.Button>("yes"));
            Submit(column.Q<UnityEngine.UIElements.Button>("again"));

            AdvanceToCompleted(MessageOf(column));

            Assert.IsTrue(DialogOf(column).isOpen);
        }

        // ルームの pages は 3 列とも（素の列も）に出る。「もう一度」でも同じ pages の 1 ページ目に戻る
        [UnityTest]
        public IEnumerator SelectRoom_WithPages_ShowsRoomPagesInAllColumns()
        {
            var pages = new[] { new MessageWindow.Page(null, "page one"), new MessageWindow.Page(null, "page two") };
            Register(new ModelRoom { displayName = "A", className = RoomA });
            Register(new ModelRoom { displayName = "B", className = RoomB, pages = pages });
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);

            controller.SelectRoom(RoomB);

            foreach (var column in Columns(controller))
            {
                var message = MessageOf(column);
                Assert.AreEqual(pages.Length, message.pageCount);
                Assert.AreEqual(pages[0].text, message.text);
                AdvanceToCompleted(message);
                Submit(column.Q<UnityEngine.UIElements.Button>("again"));
                Assert.AreEqual(pages[0].text, message.text);
            }
        }

        // pages を持たないルームは、既定の文面を出す（pages を持つルームから選び直しても残らない）
        [UnityTest]
        public IEnumerator SelectRoom_WithoutPages_ShowsDefaultPages()
        {
            Register(new ModelRoom { displayName = "A", className = RoomA, pages = new[] { new MessageWindow.Page(null, "page one") } });
            Register(new ModelRoom { displayName = "B", className = RoomB });
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);

            controller.SelectRoom(RoomB);

            foreach (var column in Columns(controller))
                Assert.AreNotEqual("page one", MessageOf(column).text);
        }

        static Label QuestionOf(VisualElement column) => DialogOf(column).Q<Label>(className: "hone-dialog__description");

        // (F) 選択肢の文面（問い、yes、no）を持つルームは、3 列とも（素の列も）その文面になる。押したボタンの文面が見出しの結果に出る
        [UnityTest]
        public IEnumerator SelectRoom_WithChoiceTexts_AppliesToAllColumns()
        {
            Register(new ModelRoom { displayName = "A", className = RoomA });
            Register(new ModelRoom { displayName = "B", className = RoomB, question = "question?", yes = "yes text", no = "no text" });
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);

            controller.SelectRoom(RoomB);

            var columns = Columns(controller);
            for (var i = 0; i < columns.Count; i++)
            {
                Assert.AreEqual("question?", QuestionOf(columns[i]).text, $"column {i}");
                Assert.AreEqual("yes text", columns[i].Q<Hone.Button>("yes").text, $"column {i}");
                Assert.AreEqual("no text", columns[i].Q<Hone.Button>("no").text, $"column {i}");
            }
            AdvanceToCompleted(MessageOf(columns[0]));
            Submit(columns[0].Q<Hone.Button>("no"));
            Assert.AreEqual("no text", StatusOf(columns[0]).text);
        }

        // (F) 選択肢の文面を持たないルームに選び直すと、YesNo.uxml の文面に戻る
        [UnityTest]
        public IEnumerator SelectRoom_WithoutChoiceTexts_RestoresUxmlTexts()
        {
            Register(new ModelRoom { displayName = "A", className = RoomA, question = "question?", yes = "yes text", no = "no text" });
            Register(new ModelRoom { displayName = "B", className = RoomB });
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);
            // 前提: 最初に組まれたルーム A の文面が当たっている
            Assert.AreEqual("yes text", Columns(controller)[0].Q<Hone.Button>("yes").text);

            controller.SelectRoom(RoomB);

            foreach (var column in Columns(controller))
            {
                Assert.AreEqual("この内容で 記録しますか？", QuestionOf(column).text);
                Assert.AreEqual("はい", column.Q<Hone.Button>("yes").text);
                Assert.AreEqual("いいえ", column.Q<Hone.Button>("no").text);
            }
        }

        // ルームを、先頭ではないルームとして選ぶ（最初の組み立てではなく、選び直した結果を見る）
        IEnumerator LoadAndSelect(ModelRoom room, Action<ModelRoomsController> onReady)
        {
            Register(new ModelRoom { displayName = "A", className = RoomA });
            Register(room);
            ModelRoomsController controller = null;
            yield return LoadScene(c => controller = c);
            controller.SelectRoom(room.className);
            onReady(controller);
        }

        IEnumerator LoadAndSelectClassicJrpg(Action<ModelRoomsController> onReady) => LoadAndSelect(ClassicJrpgRoom.Room, onReady);

        // 本文の Label（.hone-message-window__text）が使っているフォント。先頭の .hone-text は名札なので、本文のクラスで引く
        static UnityEngine.TextCore.Text.FontAsset BodyFont(VisualElement column) =>
            MessageOf(column).Q(className: "hone-message-window__text").resolvedStyle.unityFontDefinition.fontAsset;

        // (A) 2 列目と 3 列目の本文だけが、ルームのフォントになる。組み直した列のスタイルは後の update で解決されるので、ルームのフォントになるまで待つ
        [UnityTest]
        public IEnumerator ClassicJrpg_Select_AppliesRoomFontToColumns2And3Only()
        {
            ModelRoomsController controller = null;
            yield return LoadAndSelectClassicJrpg(c => controller = c);
            var expected = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>(RoomFontPath);
            Assert.IsNotNull(expected, $"{RoomFontPath} was not found");
            var columns = Columns(controller);
            var frames = 0;
            while (BodyFont(columns[1]) != expected && frames < MaxWaitFrames)
            {
                frames++;
                yield return null;
            }

            Assert.AreNotSame(expected, BodyFont(columns[0]));
            Assert.AreSame(expected, BodyFont(columns[1]));
            Assert.AreSame(expected, BodyFont(columns[2]));
        }

        // (C) 演出の設定（文字送りと確定の間）が入るのは 3 列目だけ。2 列目（USS だけ）は動かない
        [UnityTest]
        public IEnumerator ClassicJrpg_Select_InjectsRevealAndCommitDelayToColumn3Only()
        {
            ModelRoomsController controller = null;
            yield return LoadAndSelectClassicJrpg(c => controller = c);

            var columns = Columns(controller);
            Assert.AreEqual(0f, MessageOf(columns[0]).charactersPerSecond);
            Assert.AreEqual(0f, MessageOf(columns[1]).charactersPerSecond);
            Assert.Greater(MessageOf(columns[2]).charactersPerSecond, 0f);
            foreach (var buttonName in new[] { "yes", "no" })
            {
                Assert.AreEqual(0f, columns[0].Q<Hone.Button>(buttonName).commitDelay, buttonName);
                Assert.AreEqual(0f, columns[1].Q<Hone.Button>(buttonName).commitDelay, buttonName);
                Assert.Greater(columns[2].Q<Hone.Button>(buttonName).commitDelay, 0f, buttonName);
            }
        }

        // (A) テキストアドベンチャー風: 2 列目と 3 列目の本文だけが、ルームのフォント（DotGothic16）になる
        [UnityTest]
        public IEnumerator TextAdventure_Select_AppliesRoomFontToColumns2And3Only()
        {
            ModelRoomsController controller = null;
            yield return LoadAndSelect(TextAdventureRoom.Room, c => controller = c);
            var expected = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>(TextAdventureFontPath);
            Assert.IsNotNull(expected, $"{TextAdventureFontPath} was not found");
            var columns = Columns(controller);
            var frames = 0;
            while (BodyFont(columns[1]) != expected && frames < MaxWaitFrames)
            {
                frames++;
                yield return null;
            }

            Assert.AreNotSame(expected, BodyFont(columns[0]));
            Assert.AreSame(expected, BodyFont(columns[1]));
            Assert.AreSame(expected, BodyFont(columns[2]));
        }

        // テキストアドベンチャー風: ページの話者が 3 列とも名札に出る（素の列も同じ文面）
        [UnityTest]
        public IEnumerator TextAdventure_Select_ShowsSpeakerInAllColumns()
        {
            ModelRoomsController controller = null;
            yield return LoadAndSelect(TextAdventureRoom.Room, c => controller = c);

            foreach (var column in Columns(controller))
            {
                var message = MessageOf(column);
                Assert.IsTrue(message.ClassListContains("is-speaker-set"));
                Assert.AreEqual(TextAdventureRoom.Room.pages[0].speaker, message.Q<Label>(className: "hone-message-window__speaker").text);
            }
        }

        // (B) テキストアドベンチャー風: 演出の設定（文字送りと確定の間）が入るのは 3 列目だけ
        [UnityTest]
        public IEnumerator TextAdventure_Select_InjectsRevealAndCommitDelayToColumn3Only()
        {
            ModelRoomsController controller = null;
            yield return LoadAndSelect(TextAdventureRoom.Room, c => controller = c);

            var columns = Columns(controller);
            Assert.AreEqual(0f, MessageOf(columns[0]).charactersPerSecond);
            Assert.AreEqual(0f, MessageOf(columns[1]).charactersPerSecond);
            Assert.Greater(MessageOf(columns[2]).charactersPerSecond, 0f);
            foreach (var buttonName in new[] { "yes", "no" })
            {
                Assert.AreEqual(0f, columns[0].Q<Hone.Button>(buttonName).commitDelay, buttonName);
                Assert.AreEqual(0f, columns[1].Q<Hone.Button>(buttonName).commitDelay, buttonName);
                Assert.Greater(columns[2].Q<Hone.Button>(buttonName).commitDelay, 0f, buttonName);
            }
        }
    }
}
#endif
