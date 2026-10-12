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
    // Editor で実行する前提（UXML と PanelSettings を AssetDatabase から読む）。
    // このファイルの Button は Hone.Button を指す（Hone の入れ子の namespace なので、using UnityEngine.UIElements; があっても Hone.Button が勝つ）。
    // ChangeEvent の回数を見るテストは panel に attach してから測る（panel に付いていない要素の SendEvent は届かず、0 回が常に真になる）。
    // 移動は NavigationMoveEvent を SendEvent で送る（FocusScopeTests と同じ）。フォーカスの変更は非同期なので、送った後は 1 frame 進めてから focusedElement を読む
    public class CycleTests
    {
        const string CycleUxmlPath = "Assets/Hone/UI/Cycle/Cycle.uxml";
        const string PanelSettingsPath = "Assets/Hone/Sandbox/PanelSettings.asset";
        const string SandboxUxmlPath = "Assets/Hone/Sandbox/Sandbox.uxml";
        const int MaxWaitFrames = 60;

        readonly List<Object> m_Created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in m_Created)
                Object.Destroy(obj);
            m_Created.Clear();
        }

        static Button Previous(Cycle cycle) => cycle.Q<Button>(className: "hone-cycle__previous");

        static Button Next(Cycle cycle) => cycle.Q<Button>(className: "hone-cycle__next");

        static Label ValueLabel(Cycle cycle) => cycle.Q<Label>(className: "hone-cycle__value");

        static List<string> Choices(int count) => Enumerable.Range(0, count).Select(i => $"choice-{i}").ToList();

        // (A) root に hone-cycle と hone-focusable が付き focusable。子は前の Button、Label、次の Button の順
        [Test]
        public void Structure_HasClassesAndChildren()
        {
            var cycle = new Cycle();

            Assert.IsTrue(cycle.ClassListContains("hone-cycle"));
            Assert.IsTrue(cycle.ClassListContains("hone-focusable"));
            Assert.IsTrue(cycle.focusable);

            var children = cycle.Children().ToList();
            Assert.AreEqual(3, children.Count);

            var previous = children[0] as Button;
            Assert.IsNotNull(previous, "the first child must be a Hone.Button");
            Assert.IsTrue(previous.ClassListContains("hone-cycle__previous"));
            Assert.AreEqual(Button.Variant.Ghost, previous.variant);
            Assert.IsFalse(previous.focusable, "the previous Button must not take focus");
            Assert.AreEqual("‹", previous.text);

            var label = children[1] as Label;
            Assert.IsNotNull(label, "the second child must be a Label");
            Assert.IsNotInstanceOf<Button>(label);
            Assert.IsTrue(label.ClassListContains("hone-cycle__value"));
            Assert.IsTrue(label.ClassListContains("hone-text"));

            var next = children[2] as Button;
            Assert.IsNotNull(next, "the third child must be a Hone.Button");
            Assert.IsTrue(next.ClassListContains("hone-cycle__next"));
            Assert.AreEqual(Button.Variant.Ghost, next.variant);
            Assert.IsFalse(next.focusable, "the next Button must not take focus");
            Assert.AreEqual("›", next.text);
        }

        // (B) Label は choices[value]。前の Button は value == 0 で、次の Button は最後で disabled。choices が 1 つと 0 では両方 disabled
        [TestCase(3, 0, "choice-0", false, true)]
        [TestCase(3, 1, "choice-1", true, true)]
        [TestCase(3, 2, "choice-2", true, false)]
        [TestCase(1, 0, "choice-0", false, false)]
        [TestCase(0, 0, "", false, false)]
        public void LabelAndButtons_FollowValueAndChoices(int count, int value, string expected, bool previousEnabled, bool nextEnabled)
        {
            var cycle = new Cycle { choices = Choices(count), value = value };

            Assert.AreEqual(value, cycle.value);
            Assert.AreEqual(expected, ValueLabel(cycle).text);
            Assert.AreEqual(previousEnabled, Previous(cycle).enabledSelf, "previous");
            Assert.AreEqual(nextEnabled, Next(cycle).enabledSelf, "next");
        }

        // (E) の Button の部分と、状態の表の「choices が 1 つ」: wrap が true でも、2 つ以上のときだけ前後が有効
        [TestCase(3, 0, true)]
        [TestCase(3, 2, true)]
        [TestCase(1, 0, false)]
        [TestCase(0, 0, false)]
        public void Wrap_ButtonsEnabledWhenTwoOrMore(int count, int value, bool enabled)
        {
            var cycle = new Cycle { choices = Choices(count), wrap = true, value = value };

            Assert.AreEqual(enabled, Previous(cycle).enabledSelf, "previous");
            Assert.AreEqual(enabled, Next(cycle).enabledSelf, "next");
        }

        // (I) Cycle.uxml（choices="Low,Medium,High" value="1"）を Instantiate すると、choices が 3 つ、value が 1、Label が Medium
        [Test]
        public void Uxml_ChoicesAndValue_AreApplied()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(CycleUxmlPath);
            Assert.IsNotNull(tree, $"{CycleUxmlPath} was not found");

            var root = tree.Instantiate();
            var cycle = root.Q<Cycle>();

            Assert.IsNotNull(cycle, "no Hone.Cycle in Cycle.uxml");
            CollectionAssert.AreEqual(new[] { "Low", "Medium", "High" }, cycle.choices);
            Assert.AreEqual(1, cycle.value);
            Assert.AreEqual("Medium", ValueLabel(cycle).text);
        }

        // panel に付いた要素でないと SendEvent が届かないので、PanelRenderer を 1 つ作り、panel に付いた root を onReady に渡す。
        // PanelSettings の複製ごとに別の panel になる（同じ asset を共有すると、フォーカスの状態がテスト間で残る）
        IEnumerator CreatePanel(Action<VisualElement> onReady)
        {
            var panelSettings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath));
            m_Created.Add(panelSettings);
            var go = new GameObject("CycleTests");
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

        // layout が済んで worldBound が使えるまで待つ。layout 前は worldBound が 0 で、既定のナビがどこへも移さず、移動しないことを見るテストが空振りで通る
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

        // 横並びの行。左から順に足す
        static VisualElement Row(VisualElement root, params VisualElement[] children)
        {
            var row = new VisualElement { name = "row" };
            row.style.flexDirection = FlexDirection.Row;
            foreach (var child in children)
                row.Add(child);
            root.Add(row);
            return row;
        }

        static Focusable Focused(VisualElement element) => element.panel.focusController.focusedElement;

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

        static IEnumerator FocusAndWait(VisualElement element)
        {
            element.Focus();
            yield return null;
            Assert.AreSame(element, Focused(element), $"could not focus {element.name}");
        }

        static List<(int previous, int next)> RecordChanges(Cycle cycle)
        {
            var events = new List<(int previous, int next)>();
            cycle.RegisterValueChangedCallback(evt => events.Add((evt.previousValue, evt.newValue)));
            return events;
        }

        // (C) root にフォーカスがある状態で Right を送ると value が 1 増え、ChangeEvent<int> が 1 回出る。1 frame 後もフォーカスは root のまま。Left で 1 減る
        [UnityTest]
        public IEnumerator Move_RightAndLeft_ChangesValue_KeepsFocus()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var cycle = new Cycle { name = "cycle", choices = Choices(3), value = 1 };
            var events = RecordChanges(cycle);
            var left = new Button { name = "left", text = "left" };
            var right = new Button { name = "right", text = "right" };
            Row(root, left, cycle, right);
            yield return WaitForLayout(left, cycle, right);
            yield return FocusAndWait(cycle);

            SendMove(cycle, NavigationMoveEvent.Direction.Right);
            yield return null;

            Assert.AreEqual(2, cycle.value);
            CollectionAssert.AreEqual(new[] { (1, 2) }, events);
            Assert.AreEqual("choice-2", ValueLabel(cycle).text);
            Assert.AreSame(cycle, Focused(cycle), "Right must not move the focus out of the Cycle");

            events.Clear();
            SendMove(cycle, NavigationMoveEvent.Direction.Left);
            yield return null;

            Assert.AreEqual(1, cycle.value);
            CollectionAssert.AreEqual(new[] { (2, 1) }, events);
            Assert.AreSame(cycle, Focused(cycle), "Left must not move the focus out of the Cycle");
        }

        // (D) wrap が false で最後のとき Right を送っても value は変わらず、ChangeEvent は 0 回、フォーカスは右の要素へ移らない。最初のときの Left も同じ
        [UnityTest]
        public IEnumerator Move_AtEndWithoutWrap_IsConsumed_DoesNotMoveFocus()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var cycle = new Cycle { name = "cycle", choices = Choices(3), value = 2 };
            var events = RecordChanges(cycle);
            var left = new Button { name = "left", text = "left" };
            var right = new Button { name = "right", text = "right" };
            Row(root, left, cycle, right);
            yield return WaitForLayout(left, cycle, right);

            // 対照: この行では、既定のナビが左右へフォーカスを移す（left から Right で Cycle に入る）
            yield return FocusAndWait(left);
            SendMove(left, NavigationMoveEvent.Direction.Right);
            yield return null;
            Assert.AreSame(cycle, Focused(cycle), "default navigation must move Right from the left Button into the Cycle");
            CollectionAssert.IsEmpty(events);

            SendMove(cycle, NavigationMoveEvent.Direction.Right);
            yield return null;

            Assert.AreEqual(2, cycle.value);
            CollectionAssert.IsEmpty(events, "Right at the last choice must not fire a ChangeEvent");
            Assert.AreSame(cycle, Focused(cycle), "Right at the last choice must not move the focus to the right Button");

            cycle.SetValueWithoutNotify(0);
            SendMove(cycle, NavigationMoveEvent.Direction.Left);
            yield return null;

            Assert.AreEqual(0, cycle.value);
            CollectionAssert.IsEmpty(events, "Left at the first choice must not fire a ChangeEvent");
            Assert.AreSame(cycle, Focused(cycle), "Left at the first choice must not move the focus to the left Button");
        }

        // (E) wrap が true で最後のとき Right で 0 になり、ChangeEvent が 1 回出る。0 のとき Left で最後になる。前後の Button は両方 enabled
        [UnityTest]
        public IEnumerator Move_AtEndWithWrap_Wraps()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var cycle = new Cycle { name = "cycle", choices = Choices(3), wrap = true, value = 2 };
            var events = RecordChanges(cycle);
            var right = new Button { name = "right", text = "right" };
            Row(root, cycle, right);
            yield return WaitForLayout(cycle, right);
            yield return FocusAndWait(cycle);

            Assert.IsTrue(Previous(cycle).enabledSelf, "previous");
            Assert.IsTrue(Next(cycle).enabledSelf, "next");

            SendMove(cycle, NavigationMoveEvent.Direction.Right);
            yield return null;

            Assert.AreEqual(0, cycle.value);
            CollectionAssert.AreEqual(new[] { (2, 0) }, events);
            Assert.AreSame(cycle, Focused(cycle));
            Assert.IsTrue(Previous(cycle).enabledSelf, "previous");
            Assert.IsTrue(Next(cycle).enabledSelf, "next");

            events.Clear();
            SendMove(cycle, NavigationMoveEvent.Direction.Left);
            yield return null;

            Assert.AreEqual(2, cycle.value);
            CollectionAssert.AreEqual(new[] { (0, 2) }, events);
            Assert.AreSame(cycle, Focused(cycle));
        }

        // (F) 前後の Button の Submit（clicked）で、左右と同じに value が動き ChangeEvent が 1 回出る。wrap も効く
        [UnityTest]
        public IEnumerator Buttons_Submit_ChangesValue()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var cycle = new Cycle { choices = Choices(3), value = 1 };
            var events = RecordChanges(cycle);
            root.Add(cycle);
            yield return null;

            Submit(Next(cycle));
            yield return null;

            Assert.AreEqual(2, cycle.value);
            CollectionAssert.AreEqual(new[] { (1, 2) }, events);

            events.Clear();
            Submit(Previous(cycle));
            yield return null;

            Assert.AreEqual(1, cycle.value);
            CollectionAssert.AreEqual(new[] { (2, 1) }, events);

            // wrap のとき、最後で次の Button を押すと 0 へ巡る
            cycle.wrap = true;
            cycle.SetValueWithoutNotify(2);
            events.Clear();
            Submit(Next(cycle));
            yield return null;

            Assert.AreEqual(0, cycle.value);
            CollectionAssert.AreEqual(new[] { (2, 0) }, events);
        }

        // (G) SetValueWithoutNotify で Label と Button が変わり、ChangeEvent は 0 回。範囲外（-1、choices.Count）は 0 と最後に収まる（wrap が true でも）
        [UnityTest]
        public IEnumerator SetValueWithoutNotify_Clamps_WithoutChangeEvent()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var cycle = new Cycle { choices = Choices(3), value = 1 };
            var events = RecordChanges(cycle);
            root.Add(cycle);
            yield return null;

            cycle.SetValueWithoutNotify(2);
            yield return null;
            Assert.AreEqual(2, cycle.value);
            Assert.AreEqual("choice-2", ValueLabel(cycle).text);
            Assert.IsFalse(Next(cycle).enabledSelf);

            cycle.SetValueWithoutNotify(-1);
            yield return null;
            Assert.AreEqual(0, cycle.value);
            Assert.AreEqual("choice-0", ValueLabel(cycle).text);
            Assert.IsFalse(Previous(cycle).enabledSelf);

            cycle.wrap = true;
            cycle.SetValueWithoutNotify(3);
            yield return null;
            Assert.AreEqual(2, cycle.value, "SetValueWithoutNotify must clamp, not wrap");

            cycle.SetValueWithoutNotify(-1);
            yield return null;
            Assert.AreEqual(0, cycle.value, "SetValueWithoutNotify must clamp, not wrap");

            CollectionAssert.IsEmpty(events, "SetValueWithoutNotify must not fire a ChangeEvent");

            // value の set も巡らずに収める。値が変わる set なので ChangeEvent は出る（対照）
            cycle.value = 3;
            yield return null;
            Assert.AreEqual(2, cycle.value, "the value setter must clamp, not wrap");
            CollectionAssert.AreEqual(new[] { (0, 2) }, events, "a real change must fire a ChangeEvent");
        }

        // (H) choices を今の value より短くすると value が最後に収まり、ChangeEvent は 0 回。null は空として扱う
        [UnityTest]
        public IEnumerator Choices_Shrink_ClampsValue_NullIsEmpty()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var cycle = new Cycle { choices = Choices(5), value = 4 };
            var changes = 0;
            cycle.RegisterValueChangedCallback(evt => changes++);
            root.Add(cycle);
            yield return null;

            cycle.choices = Choices(2);
            yield return null;

            Assert.AreEqual(1, cycle.value);
            Assert.AreEqual("choice-1", ValueLabel(cycle).text);
            Assert.IsFalse(Next(cycle).enabledSelf);
            Assert.AreEqual(0, changes, "shrinking choices must not fire a ChangeEvent");

            cycle.choices = null;
            yield return null;

            Assert.IsNotNull(cycle.choices);
            Assert.AreEqual(0, cycle.choices.Count);
            Assert.AreEqual(0, cycle.value);
            Assert.AreEqual("", ValueLabel(cycle).text);
            Assert.IsFalse(Previous(cycle).enabledSelf);
            Assert.IsFalse(Next(cycle).enabledSelf);
            Assert.AreEqual(0, changes, "clearing choices must not fire a ChangeEvent");

            // 対照: callback が配線されていて、値が変わる set なら ChangeEvent が出る
            cycle.choices = Choices(3);
            cycle.value = 2;
            yield return null;
            Assert.AreEqual(1, changes, "a real change must fire a ChangeEvent");
        }

        // (J) FocusScope { trap = true } の中で、左右は Cycle が消費して value が動きフォーカスは root のまま。Down は消費せず、trap が下の Button へ移す
        [UnityTest]
        public IEnumerator InsideTrap_LeftRightConsumed_DownLeaves()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var scope = new FocusScope { name = "scope", trap = true, autoFocus = false };
            var cycle = new Cycle { name = "cycle", choices = Choices(3), value = 1 };
            var events = RecordChanges(cycle);
            var right = new Button { name = "right", text = "right" };
            var below = new Button { name = "below", text = "below" };
            var row = new VisualElement { name = "row" };
            row.style.flexDirection = FlexDirection.Row;
            row.Add(cycle);
            row.Add(right);
            scope.Add(row);
            scope.Add(below);
            root.Add(scope);
            yield return WaitForLayout(cycle, right, below);
            yield return FocusAndWait(cycle);

            SendMove(cycle, NavigationMoveEvent.Direction.Right);
            yield return null;

            Assert.AreEqual(2, cycle.value);
            CollectionAssert.AreEqual(new[] { (1, 2) }, events);
            Assert.AreSame(cycle, Focused(cycle), "the trap must leave Right to the Cycle");

            // 端でも、trap が右の Button へ移さない
            events.Clear();
            SendMove(cycle, NavigationMoveEvent.Direction.Right);
            yield return null;

            Assert.AreEqual(2, cycle.value);
            CollectionAssert.IsEmpty(events);
            Assert.AreSame(cycle, Focused(cycle), "the trap must not move Right at the last choice");

            SendMove(cycle, NavigationMoveEvent.Direction.Left);
            yield return null;

            Assert.AreEqual(1, cycle.value);
            Assert.AreSame(cycle, Focused(cycle), "the trap must leave Left to the Cycle");

            events.Clear();
            SendMove(cycle, NavigationMoveEvent.Direction.Down);
            yield return null;

            Assert.AreEqual(1, cycle.value, "Down must not change the value");
            CollectionAssert.IsEmpty(events);
            Assert.AreSame(below, Focused(cycle), "the trap must still move Down out of the Cycle");
        }
    }
}
#endif
