#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    // Tabs と ToggleGroup は Hone のもの（Hone の入れ子の namespace なので修飾しなくても Hone.Tabs を指す）。Button は Unity 標準と紛らわしいので Hone.Button と修飾して書く。
    // 移動は NavigationMoveEvent を SendEvent で送る（FocusScopeTests と同じ）。フォーカスの変更は非同期なので、送った後は 1 frame 進めてから focusedElement を読む。
    // コメントの (A)〜(D) は Issue #112 の受け入れ条件の番号
    public class TabsTests
    {
        const string TabsUxmlPath = "Assets/Hone/UI/Tabs/Tabs.uxml";
        const string PanelSettingsPath = "Assets/Hone/Sandbox/PanelSettings.asset";
        const string WorldSpacePanelSettingsPath = "Assets/Hone/Sandbox/WorldSpacePanelSettings.asset";
        const string SandboxUxmlPath = "Assets/Hone/Sandbox/Sandbox.uxml";
        const int MaxWaitFrames = 60;
        const int SettleFrames = 2;
        // value を tabs より前に書いた UXML を置く一時ディレクトリ（TearDown で消す）
        const string TmpDir = "Assets/TabsTestsTmp";

        readonly List<Object> m_Created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in m_Created)
                Object.Destroy(obj);
            m_Created.Clear();
            AssetDatabase.DeleteAsset(TmpDir);
        }

        // PanelRenderer を 1 つ作り、UI が読み込まれたら root を返す（DialogTests と同じ）。
        // World Space は WorldSpacePanelSettings.asset に worldSpaceSizeMode = Fixed の 1920×1080。
        // asset の Render Mode が worldSpace と食い違っていたら落とす（World Space のテストが Screen Space の panel で黙って通らないように）
        IEnumerator CreatePanel(bool worldSpace, Action<VisualElement> onReady)
        {
            var path = worldSpace ? WorldSpacePanelSettingsPath : PanelSettingsPath;
            var source = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            Assert.IsNotNull(source, $"{path} was not found");
            Assert.AreEqual(worldSpace ? PanelRenderMode.WorldSpace : PanelRenderMode.ScreenSpaceOverlay, source.renderMode, $"{path} has an unexpected render mode");
            var panelSettings = Object.Instantiate(source);
            m_Created.Add(panelSettings);
            var go = new GameObject("TabsTests");
            m_Created.Add(go);
            go.SetActive(false);
            var panelRenderer = go.AddComponent<PanelRenderer>();
            panelRenderer.panelSettings = panelSettings;
            panelRenderer.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SandboxUxmlPath);
            if (worldSpace)
            {
                panelRenderer.worldSpaceSizeMode = WorldSpaceSizeMode.Fixed;
                panelRenderer.worldSpaceSize = new Vector2(1920, 1080);
            }
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

        static Focusable Focused(VisualElement element) => element.panel.focusController.focusedElement;

        static IEnumerator FocusAndWait(VisualElement element)
        {
            element.Focus();
            yield return null;
            Assert.AreSame(element, Focused(element), $"could not focus {element.name}");
        }

        // タブの Button にフォーカスがあるとき、focusedElement は Button ではなくタブの列の ToggleGroup になる（ToggleButtonGroup が composite root のため。6000.7.0b4 で実測）。
        // どのタブにフォーカスがあるかは、各タブに最後に届いた FocusEvent で見る
        class TabFocus
        {
            public VisualElement Last;
        }

        static TabFocus RecordTabFocus(IEnumerable<Hone.Button> triggers)
        {
            var record = new TabFocus();
            foreach (var trigger in triggers)
                trigger.RegisterCallback<FocusEvent>(evt => record.Last = (VisualElement)evt.currentTarget);
            return record;
        }

        static void AssertTabFocused(Tabs tabs, TabFocus record, Hone.Button expected, string message)
        {
            Assert.AreSame(tabs.Q<ToggleGroup>(), Focused(tabs), message + " (the focus must be in the tab list)");
            Assert.AreSame(expected, record.Last, message);
        }

        static IEnumerator FocusTabAndWait(Tabs tabs, TabFocus record, Hone.Button trigger)
        {
            trigger.Focus();
            yield return null;
            AssertTabFocused(tabs, record, trigger, $"could not focus the tab {trigger.text}");
        }

        static void SendMove(VisualElement target, NavigationMoveEvent.Direction direction)
        {
            using (var evt = NavigationMoveEvent.GetPooled(direction))
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        static void Submit(VisualElement target)
        {
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        // 中央を押して離す（DialogTests の Click と同じ組み方。Screen Space の panel で使う）
        static void Click(VisualElement target)
        {
            var position = target.worldBound.center;
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

        static List<Hone.Button> Triggers(Tabs tabs) => tabs.Q<ToggleGroup>(className: "hone-tabs__list").Query<Hone.Button>().ToList();

        static List<VisualElement> Contents(Tabs tabs) => tabs.Query<VisualElement>(className: "hone-tabs__content").ToList();

        static int[] DisplayedContents(Tabs tabs) =>
            Contents(tabs).Select((c, i) => (c, i)).Where(x => x.c.resolvedStyle.display != DisplayStyle.None).Select(x => x.i).ToArray();

        // 3 つのタブ。中身はそれぞれ 2 段で、1 段目は左右に半分ずつの Hone.Button（left が DFS の最初）、2 段目は全幅の Hone.Button。
        // (B) は、既定のナビなら別の行き先になる組み合わせで確かめる。右端のタブから Down なら既定は右の Button、
        // 1 段目の右の Button から Up なら既定は右寄りのタブ、左の Button から Up なら左寄りのタブへ移る（それぞれ Tabs の処理を外すと落ちることを 6000.7.0b4 で確かめた）
        static Tabs CreateTabs()
        {
            var tabs = new Tabs { tabs = new List<string> { "Account", "Password", "Other" } };
            for (var i = 0; i < 3; i++)
            {
                var content = new VisualElement { name = "content-" + i };
                var row = new VisualElement { name = $"content-{i}-row-0" };
                row.style.flexDirection = FlexDirection.Row;
                row.Add(new Hone.Button { name = $"content-{i}-row-0-left", text = "left", style = { flexGrow = 1, flexBasis = 0 } });
                row.Add(new Hone.Button { name = $"content-{i}-row-0-right", text = "right", style = { flexGrow = 1, flexBasis = 0 } });
                content.Add(row);
                content.Add(new Hone.Button { name = $"content-{i}-row-1", text = $"content {i} row 1" });
                tabs.Add(content);
            }
            return tabs;
        }

        // (A)(D) Tabs.uxml を読むと、タブと中身の数が揃い、最初の中身だけが表示される
        [UnityTest]
        public IEnumerator Uxml_ShowsOnlyFirstContent()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TabsUxmlPath);
            Assert.IsNotNull(tree, $"{TabsUxmlPath} was not found");
            var instance = tree.Instantiate();
            var tabs = instance.Q<Tabs>();
            Assert.IsNotNull(tabs, "Tabs.uxml must have a Tabs");

            VisualElement root = null;
            yield return CreatePanel(false, r => root = r);
            root.Add(instance);
            yield return Settle();

            Assert.IsTrue(tabs.ClassListContains("hone-tabs"));
            var triggers = Triggers(tabs);
            Assert.Greater(triggers.Count, 1, "Tabs.uxml must have 2 or more tabs");
            Assert.AreEqual(triggers.Count, Contents(tabs).Count, "Tabs.uxml must have one content per tab");
            Assert.AreEqual(0, tabs.value);
            CollectionAssert.AreEqual(new[] { 0 }, DisplayedContents(tabs));
        }

        // (A) Submit でタブを選ぶと、その中身だけが表示され、ChangeEvent<int> が 1 回出る。表示中の中身にだけ is-active が付く
        [UnityTest]
        public IEnumerator Submit_ShowsOnlyThatContent_AndFiresChangeOnce()
        {
            VisualElement root = null;
            yield return CreatePanel(false, r => root = r);
            var tabs = CreateTabs();
            root.Add(tabs);
            yield return Settle();
            CollectionAssert.AreEqual(new[] { 0 }, DisplayedContents(tabs), "precondition: only the first content is shown");

            var changes = new List<int>();
            tabs.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == tabs)
                    changes.Add(evt.newValue);
            });

            Submit(Triggers(tabs)[2]);
            yield return Settle();

            Assert.AreEqual(2, tabs.value);
            CollectionAssert.AreEqual(new[] { 2 }, DisplayedContents(tabs));
            CollectionAssert.AreEqual(new[] { 2 }, changes);
            var contents = Contents(tabs);
            Assert.IsTrue(contents[2].ClassListContains("is-active"));
            Assert.IsFalse(contents[0].ClassListContains("is-active"));
        }

        // (A) Pointer でタブを押すと、その中身だけが表示される
        [UnityTest]
        public IEnumerator Pointer_ShowsOnlyThatContent()
        {
            VisualElement root = null;
            yield return CreatePanel(false, r => root = r);
            var tabs = CreateTabs();
            root.Add(tabs);
            var triggers = Triggers(tabs);
            yield return WaitForLayout(triggers.ToArray());

            Click(triggers[1]);
            yield return Settle();

            Assert.AreEqual(1, tabs.value);
            CollectionAssert.AreEqual(new[] { 1 }, DisplayedContents(tabs));
        }

        // (A) C# から value を変えても、その中身だけが表示され、ChangeEvent<int> が 1 回出る。SetValueWithoutNotify は表示だけを変える
        [UnityTest]
        public IEnumerator Value_ShowsOnlyThatContent()
        {
            VisualElement root = null;
            yield return CreatePanel(false, r => root = r);
            var tabs = CreateTabs();
            root.Add(tabs);
            yield return Settle();
            var changes = 0;
            tabs.RegisterValueChangedCallback(evt => changes++);

            tabs.value = 2;
            yield return Settle();
            CollectionAssert.AreEqual(new[] { 2 }, DisplayedContents(tabs));
            Assert.AreEqual(1, changes);

            tabs.SetValueWithoutNotify(1);
            yield return Settle();
            CollectionAssert.AreEqual(new[] { 1 }, DisplayedContents(tabs));
            Assert.AreEqual(1, changes, "SetValueWithoutNotify must not fire a ChangeEvent");
            // 選択中のタブも揃う
            var state = tabs.Q<ToggleGroup>().value;
            Assert.IsTrue(state[1]);
            Assert.IsFalse(state[2]);
        }

        // (B) タブの列から Down で、表示中の中身の最初の focusable（1 段目の左）に移る。右端のタブから押す（既定のナビなら真下の右の Button へ移る）
        [UnityTest]
        public IEnumerator Down_FromTab_FocusesFirstInActiveContent([Values(false, true)] bool worldSpace)
        {
            VisualElement root = null;
            yield return CreatePanel(worldSpace, r => root = r);
            var tabs = CreateTabs();
            tabs.value = 2;
            root.Add(tabs);
            var triggers = Triggers(tabs);
            var first = tabs.Q<Hone.Button>("content-2-row-0-left");
            yield return WaitForLayout(triggers[2], first);
            var record = RecordTabFocus(triggers);

            yield return FocusTabAndWait(tabs, record, triggers[2]);
            SendMove(triggers[2], NavigationMoveEvent.Direction.Down);
            yield return null;
            Assert.AreSame(first, Focused(root), "Down from a tab must focus the first focusable of the shown content");
        }

        // (B) 中身の最上段から Up で、幾何で近いタブではなく選択中のタブに戻る。選択が左端（0）なら 1 段目の右から、右端（2）なら左から押す
        [UnityTest]
        public IEnumerator Up_FromTopRow_FocusesSelectedTab([Values(false, true)] bool worldSpace, [Values(0, 2)] int selected)
        {
            VisualElement root = null;
            yield return CreatePanel(worldSpace, r => root = r);
            var tabs = CreateTabs();
            tabs.value = selected;
            root.Add(tabs);
            var triggers = Triggers(tabs);
            var from = tabs.Q<Hone.Button>($"content-{selected}-row-0-{(selected == 0 ? "right" : "left")}");
            yield return WaitForLayout(triggers[0], triggers[1], triggers[2], from);
            var record = RecordTabFocus(triggers);

            yield return FocusAndWait(from);
            SendMove(from, NavigationMoveEvent.Direction.Up);
            yield return null;
            AssertTabFocused(tabs, record, triggers[selected], "Up from the top row must focus the selected tab");
        }

        // (B) 中身の 2 段目から Up は Tabs が横取りせず、既定のナビで 1 段目（左右のどちらか）に移る
        [UnityTest]
        public IEnumerator Up_FromSecondRow_StaysInContent([Values(false, true)] bool worldSpace)
        {
            VisualElement root = null;
            yield return CreatePanel(worldSpace, r => root = r);
            var tabs = CreateTabs();
            tabs.value = 2;
            root.Add(tabs);
            var row = tabs.Q("content-2-row-0");
            var second = tabs.Q<Hone.Button>("content-2-row-1");
            yield return WaitForLayout(row, second);

            yield return FocusAndWait(second);
            SendMove(second, NavigationMoveEvent.Direction.Up);
            yield return null;
            var focused = Focused(root) as VisualElement;
            Assert.IsTrue(focused != null && focused.parent == row, $"Up from the second row must move to the row above in the content, but focused {focused?.name}");
        }

        // 選択の変化を数える。中身の要素の ChangeEvent<int> も Tabs まで上がるので、target が Tabs のものだけ数える
        static List<int> RecordChanges(Tabs tabs)
        {
            var changes = new List<int>();
            tabs.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == tabs)
                    changes.Add(evt.newValue);
            });
            return changes;
        }

        // tabs を set し直しても value と選択は残り、ChangeEvent は出ない。数を減らすと value は範囲に収まり、そのときも ChangeEvent は出ない
        [UnityTest]
        public IEnumerator SetTabs_KeepsValue_AndShrinkClamps()
        {
            VisualElement root = null;
            yield return CreatePanel(false, r => root = r);
            var tabs = CreateTabs();
            root.Add(tabs);
            yield return Settle();
            tabs.value = 2;
            yield return Settle();
            var changes = RecordChanges(tabs);

            tabs.tabs = new List<string> { "A", "B", "C" };
            yield return Settle();
            Assert.AreEqual(2, tabs.value);
            Assert.IsTrue(tabs.Q<ToggleGroup>().value[2], "the selected tab must survive the rebuild");
            Assert.IsFalse(tabs.Q<ToggleGroup>().value[0]);
            CollectionAssert.AreEqual(new[] { 2 }, DisplayedContents(tabs));

            tabs.tabs = new List<string> { "A", "B" };
            yield return Settle();
            Assert.AreEqual(1, tabs.value);
            Assert.IsTrue(tabs.Q<ToggleGroup>().value[1]);
            CollectionAssert.AreEqual(new[] { 1 }, DisplayedContents(tabs));
            CollectionAssert.IsEmpty(changes, "setting tabs must not fire a ChangeEvent");
        }

        // tabs を 3 個から 2 個、2 個から 4 個に set し直したあと（6000.7.0b4 では、タブの列の length が子の数になる作り直しになる）も、タブの列で選ばれているのは value の 1 つだけで、その中身だけが表示される。
        // 伸ばしたあとの最後のタブも選べる
        [UnityTest]
        public IEnumerator SetTabs_ToOtherCount_SelectsOnlyValue_AndShowsOnlyThatContent()
        {
            VisualElement root = null;
            yield return CreatePanel(false, r => root = r);
            var tabs = new Tabs { tabs = new List<string> { "A", "B", "C" } };
            for (var i = 0; i < 4; i++)
                tabs.Add(new VisualElement { name = "content-" + i });
            root.Add(tabs);
            yield return Settle();
            tabs.value = 2;
            yield return Settle();
            Assert.AreEqual(2, tabs.value, "precondition");
            CollectionAssert.AreEqual(new[] { 2 }, DisplayedContents(tabs), "precondition");
            var changes = RecordChanges(tabs);

            tabs.tabs = new List<string> { "A", "B" };
            yield return Settle();
            AssertOnlyValueSelected(tabs, 1, 2);

            tabs.tabs = new List<string> { "A", "B", "C", "D" };
            yield return Settle();
            AssertOnlyValueSelected(tabs, 1, 4);
            CollectionAssert.IsEmpty(changes, "setting tabs must not fire a ChangeEvent");

            tabs.value = 3;
            yield return Settle();
            AssertOnlyValueSelected(tabs, 3, 4);
            CollectionAssert.AreEqual(new[] { 3 }, changes);
        }

        static void AssertOnlyValueSelected(Tabs tabs, int expectedValue, int expectedTabCount)
        {
            Assert.AreEqual(expectedTabCount, Triggers(tabs).Count);
            Assert.AreEqual(expectedValue, tabs.value);
            var state = tabs.Q<ToggleGroup>(className: "hone-tabs__list").value;
            Assert.GreaterOrEqual(state.length, expectedTabCount, "the tab list state must cover every tab");
            var selected = new List<int>();
            for (var i = 0; i < state.length; i++)
            {
                if (state[i])
                    selected.Add(i);
            }
            CollectionAssert.AreEqual(new[] { expectedValue }, selected, "exactly the tab at value must be selected");
            CollectionAssert.AreEqual(new[] { expectedValue }, DisplayedContents(tabs));
        }

        // 別のイベントの配送中（Tabs の ChangeEvent の callback の中）に tabs を set し直しても、value は戻らず、ChangeEvent は Submit の 1 回だけ
        [UnityTest]
        public IEnumerator SetTabs_InsideCallback_KeepsValue()
        {
            VisualElement root = null;
            yield return CreatePanel(false, r => root = r);
            var tabs = CreateTabs();
            root.Add(tabs);
            yield return Settle();
            var changes = RecordChanges(tabs);
            var rebuilt = false;
            tabs.RegisterValueChangedCallback(evt =>
            {
                if (evt.target != tabs || rebuilt)
                    return;
                rebuilt = true;
                tabs.tabs = new List<string> { "A", "B", "C" };
            });

            Submit(Triggers(tabs)[1]);
            yield return Settle();

            Assert.IsTrue(rebuilt, "precondition: tabs was set inside the callback");
            Assert.AreEqual(1, tabs.value);
            Assert.IsTrue(tabs.Q<ToggleGroup>().value[1]);
            CollectionAssert.AreEqual(new[] { 1 }, DisplayedContents(tabs));
            CollectionAssert.AreEqual(new[] { 1 }, changes);
        }

        // 選ばれているタブを押し直しても、選択は外れず、ChangeEvent は出ない
        [UnityTest]
        public IEnumerator Submit_SelectedTab_KeepsSelection()
        {
            VisualElement root = null;
            yield return CreatePanel(false, r => root = r);
            var tabs = CreateTabs();
            tabs.value = 1;
            root.Add(tabs);
            yield return Settle();
            var changes = RecordChanges(tabs);

            Submit(Triggers(tabs)[1]);
            yield return Settle();

            Assert.AreEqual(1, tabs.value);
            Assert.IsTrue(tabs.Q<ToggleGroup>().value[1], "the selected tab must stay selected");
            CollectionAssert.AreEqual(new[] { 1 }, DisplayedContents(tabs));
            CollectionAssert.IsEmpty(changes);
        }

        // value は 0〜tabs.Count - 1 に収まる。tabs が空のときは value 0 で、どの中身も表示しない
        [UnityTest]
        public IEnumerator Value_IsClamped_AndEmptyTabsShowNothing()
        {
            VisualElement root = null;
            yield return CreatePanel(false, r => root = r);
            var tabs = CreateTabs();
            root.Add(tabs);
            yield return Settle();

            tabs.value = 99;
            Assert.AreEqual(2, tabs.value);
            tabs.value = -1;
            Assert.AreEqual(0, tabs.value);

            tabs.tabs = null;
            yield return Settle();
            Assert.AreEqual(0, tabs.value);
            CollectionAssert.IsEmpty(DisplayedContents(tabs), "no content is shown when there are no tabs");
        }

        // UXML に value を tabs より前に書いても、value は残る（属性は宣言の順に適用される）
        [UnityTest]
        public IEnumerator Uxml_ValueBeforeTabs_IsKept()
        {
            AssetDatabase.DeleteAsset(TmpDir);
            AssetDatabase.CreateFolder("Assets", "TabsTestsTmp");
            var path = TmpDir + "/ValueFirst.uxml";
            File.WriteAllText(path,
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:hone=\"Hone\">\n" +
                "    <hone:Tabs value=\"1\" tabs=\"A,B\">\n" +
                "        <ui:VisualElement />\n" +
                "        <ui:VisualElement />\n" +
                "    </hone:Tabs>\n" +
                "</ui:UXML>\n");
            AssetDatabase.ImportAsset(path);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.IsNotNull(tree, $"{path} was not imported");

            VisualElement root = null;
            yield return CreatePanel(false, r => root = r);
            var instance = tree.Instantiate();
            root.Add(instance);
            yield return Settle();

            var tabs = instance.Q<Tabs>();
            Assert.AreEqual(1, tabs.value);
            CollectionAssert.AreEqual(new[] { 1 }, DisplayedContents(tabs));
        }

        // (B) 実行中に Submit でタブを切り替えた直後でも、Down は切り替えた先の中身の最初の focusable に移る
        [UnityTest]
        public IEnumerator Down_AfterSubmit_FocusesFirstInNewContent()
        {
            VisualElement root = null;
            yield return CreatePanel(false, r => root = r);
            var tabs = CreateTabs();
            root.Add(tabs);
            var triggers = Triggers(tabs);
            yield return WaitForLayout(triggers.ToArray());
            var record = RecordTabFocus(triggers);

            yield return FocusTabAndWait(tabs, record, triggers[1]);
            Submit(triggers[1]);
            yield return Settle();
            SendMove(triggers[1], NavigationMoveEvent.Direction.Down);
            yield return null;
            Assert.AreSame(tabs.Q<Hone.Button>("content-1-row-0-left"), Focused(root));
        }

        // (B) NavigationMoveEvent の target がタブの列（composite root の ToggleGroup）そのものでも、Down は中身へ移る
        [UnityTest]
        public IEnumerator Down_TargetIsList_FocusesFirstInActiveContent()
        {
            VisualElement root = null;
            yield return CreatePanel(false, r => root = r);
            var tabs = CreateTabs();
            tabs.value = 2;
            root.Add(tabs);
            var triggers = Triggers(tabs);
            yield return WaitForLayout(triggers.ToArray());
            var record = RecordTabFocus(triggers);

            yield return FocusTabAndWait(tabs, record, triggers[2]);
            SendMove(tabs.Q<ToggleGroup>(), NavigationMoveEvent.Direction.Down);
            yield return null;
            Assert.AreSame(tabs.Q<Hone.Button>("content-2-row-0-left"), Focused(root));
        }

        // 中身に focusable が無いタブから Down でも、例外を出さず、選択も変わらない（移動は既定のナビに任せる）
        [UnityTest]
        public IEnumerator Down_WithoutFocusables_LeavesItToDefault()
        {
            VisualElement root = null;
            yield return CreatePanel(false, r => root = r);
            var tabs = new Tabs { tabs = new List<string> { "A", "B" } };
            tabs.Add(new Label("a"));
            tabs.Add(new Label("b"));
            root.Add(tabs);
            var triggers = Triggers(tabs);
            yield return WaitForLayout(triggers.ToArray());
            var record = RecordTabFocus(triggers);

            yield return FocusTabAndWait(tabs, record, triggers[0]);
            SendMove(triggers[0], NavigationMoveEvent.Direction.Down);
            yield return null;
            Assert.AreEqual(0, tabs.value);
        }

        // タブにフォーカスして左右では、既定のナビで隣のタブへフォーカスが移り、選択は変わらない。
        // そのまま Down で、フォーカスしているタブではなく選択中のタブの中身へ入る
        [UnityTest]
        public IEnumerator Right_MovesFocusOnly_AndDownEntersSelectedContent()
        {
            VisualElement root = null;
            yield return CreatePanel(false, r => root = r);
            var tabs = CreateTabs();
            tabs.value = 2;
            root.Add(tabs);
            var triggers = Triggers(tabs);
            yield return WaitForLayout(triggers.ToArray());
            var record = RecordTabFocus(triggers);

            yield return FocusTabAndWait(tabs, record, triggers[0]);
            SendMove(triggers[0], NavigationMoveEvent.Direction.Right);
            yield return null;
            AssertTabFocused(tabs, record, triggers[1], "Right from the first tab must focus the second tab");
            Assert.AreEqual(2, tabs.value, "moving between tabs must not change the selection");

            SendMove(triggers[1], NavigationMoveEvent.Direction.Down);
            yield return null;
            Assert.AreSame(tabs.Q<Hone.Button>("content-2-row-0-left"), Focused(root));
        }

        // (C) 中身にフォーカスがある間も、選択中のタブに :checked が残る。:checked の背景（--hone-color-background、不透明）で測る。選ばれていないタブは透明
        [UnityTest]
        public IEnumerator FocusInContent_KeepsCheckedOnSelectedTab()
        {
            VisualElement root = null;
            yield return CreatePanel(false, r => root = r);
            var tabs = CreateTabs();
            root.Add(tabs);
            var triggers = Triggers(tabs);
            yield return WaitForLayout(triggers.ToArray());

            Submit(triggers[2]);
            yield return Settle();
            yield return FocusAndWait(tabs.Q<Hone.Button>("content-2-row-0-left"));
            yield return Settle();

            Assert.IsTrue(tabs.Q<ToggleGroup>().value[2], "the selected tab must stay selected while the content has focus");
            Assert.AreEqual(1f, triggers[2].resolvedStyle.backgroundColor.a, "the selected tab must keep the :checked background (--hone-color-background is assumed opaque; a theme change can also fail this)");
            Assert.AreEqual(0f, triggers[0].resolvedStyle.backgroundColor.a, "an unselected tab must have a transparent background");
        }
    }
}
#endif
