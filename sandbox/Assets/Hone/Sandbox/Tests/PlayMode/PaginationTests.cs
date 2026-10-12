#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
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
    // このファイルの Button は Hone.Button を指す（Hone の入れ子の namespace なので、using UnityEngine.UIElements; があっても Hone.Button が勝つ）。
    // ChangeEvent の回数を見るテストは panel に attach してから測る（panel に付いていない要素の SendEvent は届かず、0 回が常に真になる）。
    public class PaginationTests
    {
        const string PaginationUxmlPath = "Assets/Hone/UI/Pagination/Pagination.uxml";
        const string PanelSettingsPath = "Assets/Hone/Sandbox/PanelSettings.asset";
        const string SandboxUxmlPath = "Assets/Hone/Sandbox/Sandbox.uxml";
        const string CurrentClass = "is-current";
        const int MaxWaitFrames = 60;

        readonly List<Object> m_Created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in m_Created)
                Object.Destroy(obj);
            m_Created.Clear();
        }

        static VisualElement Items(Pagination pagination) =>
            pagination.Q(className: "hone-pagination__items");

        static Button Previous(Pagination pagination) =>
            pagination.Q<Button>(className: "hone-pagination__previous");

        static Button Next(Pagination pagination) =>
            pagination.Q<Button>(className: "hone-pagination__next");

        static List<Button> PageButtons(Pagination pagination) =>
            Items(pagination).Children().OfType<Button>().ToList();

        // .hone-pagination__items の子を順に読み、Button は text、省略記号の Label はその text（"…"）にしてスペースでつなぐ
        static string Sequence(Pagination pagination) =>
            string.Join(" ", Items(pagination).Children().Select(child =>
            {
                if (child is Button button)
                    return button.text;
                Assert.IsTrue(child is Label && child.ClassListContains("hone-pagination__ellipsis"),
                    $"unexpected child {child.GetType().Name} in hone-pagination__items");
                Assert.IsTrue(child.ClassListContains("hone-text"), "the ellipsis must follow the body font (.hone-text)");
                return ((Label)child).text;
            }));

        static string CurrentText(Pagination pagination) =>
            PageButtons(pagination).Single(b => b.ClassListContains(CurrentClass)).text;

        // (A) pageCount と value の組み合わせごとに、並びが「表示するページ番号の規則」の表のとおりになる
        [TestCase(5, 2, "1 2 3 4 5")]
        [TestCase(7, 3, "1 2 3 4 5 6 7")]
        [TestCase(10, 0, "1 2 3 4 5 … 10")]
        [TestCase(10, 3, "1 2 3 4 5 … 10")]
        [TestCase(10, 4, "1 … 4 5 6 … 10")]
        [TestCase(10, 5, "1 … 5 6 7 … 10")]
        [TestCase(10, 6, "1 … 6 7 8 9 10")]
        [TestCase(10, 9, "1 … 6 7 8 9 10")]
        [TestCase(8, 3, "1 2 3 4 5 … 8")]
        [TestCase(8, 4, "1 … 4 5 6 7 8")]
        [TestCase(0, 0, "")]
        public void Items_FollowVisiblePageRule(int pageCount, int value, string expected)
        {
            var pagination = new Pagination { pageCount = pageCount, value = value };

            Assert.AreEqual(expected, Sequence(pagination));
        }

        // (B) 今のページの Button だけに .is-current と variant Outline が付き、ほかは Ghost
        [TestCase(10, 0)]
        [TestCase(10, 4)]
        [TestCase(10, 9)]
        [TestCase(5, 2)]
        public void CurrentPage_IsOutlineWithCurrentClass_OthersAreGhost(int pageCount, int value)
        {
            var pagination = new Pagination { pageCount = pageCount, value = value };
            var current = (value + 1).ToString();

            foreach (var button in PageButtons(pagination))
            {
                var isCurrent = button.text == current;
                Assert.AreEqual(isCurrent, button.ClassListContains(CurrentClass), $"page {button.text}: .is-current");
                Assert.AreEqual(isCurrent ? Button.Variant.Outline : Button.Variant.Ghost, button.variant, $"page {button.text}: variant");
                Assert.IsTrue(button.ClassListContains("hone-pagination__item"), $"page {button.text}: hone-pagination__item");
            }
            Assert.AreEqual(1, PageButtons(pagination).Count(b => b.ClassListContains(CurrentClass)));
        }

        // (C) 前の Button は value == 0 で、次の Button は最後のページで disabled。pageCount が 1 と 0 では両方 disabled
        [TestCase(10, 0, false, true)]
        [TestCase(10, 4, true, true)]
        [TestCase(10, 9, true, false)]
        [TestCase(1, 0, false, false)]
        [TestCase(0, 0, false, false)]
        public void PreviousAndNext_EnabledByPosition(int pageCount, int value, bool previousEnabled, bool nextEnabled)
        {
            var pagination = new Pagination { pageCount = pageCount, value = value };

            Assert.AreEqual(previousEnabled, Previous(pagination).enabledSelf, "previous");
            Assert.AreEqual(nextEnabled, Next(pagination).enabledSelf, "next");
        }

        // 前後の Button の記号と variant
        [Test]
        public void PreviousAndNext_AreGhostWithArrows()
        {
            var pagination = new Pagination();

            Assert.IsTrue(pagination.ClassListContains("hone-pagination"));
            Assert.AreEqual("‹", Previous(pagination).text);
            Assert.AreEqual("›", Next(pagination).text);
            Assert.AreEqual(Button.Variant.Ghost, Previous(pagination).variant);
            Assert.AreEqual(Button.Variant.Ghost, Next(pagination).variant);
        }

        // (I) Pagination.uxml（page-count="10" value="4"）を Instantiate すると、page-count と value の両方が残り、pageCount 10、value 4 になる
        [Test]
        public void Uxml_PageCountAndValue_AreApplied()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PaginationUxmlPath);
            Assert.IsNotNull(tree, $"{PaginationUxmlPath} was not found");

            var root = tree.Instantiate();
            var pagination = root.Q<Pagination>();

            Assert.IsNotNull(pagination, "no Hone.Pagination in Pagination.uxml");
            Assert.AreEqual(10, pagination.pageCount);
            Assert.AreEqual(4, pagination.value);
            Assert.AreEqual("1 … 4 5 6 … 10", Sequence(pagination));
        }

        // panel に付いた要素でないと SendEvent が届かないので、PanelRenderer を 1 つ作り、panel に付いた root を onReady に渡す
        IEnumerator CreatePanel(System.Action<VisualElement> onReady)
        {
            var panelSettings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath));
            m_Created.Add(panelSettings);
            var go = new GameObject("PaginationTests");
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

        static void Submit(VisualElement target)
        {
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        // (D) 次の Button の Submit で value が 1 増え、ChangeEvent<int> が 1 回出る。番号の Button で同じくそのページへ移る。
        // 今のページの番号を押しても ChangeEvent は出ない
        [UnityTest]
        public IEnumerator Submit_MovesPage_AndFiresChangeEventOnce()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var pagination = new Pagination { pageCount = 10, value = 4 };
            var events = new List<(int previous, int next)>();
            pagination.RegisterValueChangedCallback(evt => events.Add((evt.previousValue, evt.newValue)));
            root.Add(pagination);
            yield return null;

            var previous = Previous(pagination);
            var next = Next(pagination);

            Submit(next);
            yield return null;

            Assert.AreEqual(5, pagination.value);
            CollectionAssert.AreEqual(new[] { (4, 5) }, events);
            Assert.AreEqual("1 … 5 6 7 … 10", Sequence(pagination));
            Assert.AreEqual("6", CurrentText(pagination), ".is-current must move to the new page");

            // 前後の Button は作り直さない。同じインスタンスが previous, items, next の順で残る
            Assert.AreSame(previous, Previous(pagination));
            Assert.AreSame(next, Next(pagination));
            CollectionAssert.AreEqual(new VisualElement[] { previous, Items(pagination), next }, pagination.Children().ToList());

            events.Clear();
            Submit(Previous(pagination));
            yield return null;

            Assert.AreEqual(4, pagination.value);
            CollectionAssert.AreEqual(new[] { (5, 4) }, events);

            events.Clear();
            Submit(PageButtons(pagination).First(b => b.text == "10"));
            yield return null;

            Assert.AreEqual(9, pagination.value);
            CollectionAssert.AreEqual(new[] { (4, 9) }, events);
            Assert.AreEqual("1 … 6 7 8 9 10", Sequence(pagination));
            Assert.AreEqual("10", CurrentText(pagination), ".is-current must move to the new page");

            events.Clear();
            Submit(PageButtons(pagination).First(b => b.text == "10"));
            yield return null;

            Assert.AreEqual(9, pagination.value);
            CollectionAssert.IsEmpty(events, "pressing the current page must not fire a ChangeEvent");
        }

        // (E) SetValueWithoutNotify で並びが変わり、ChangeEvent は出ない。範囲外の値（-1、pageCount）は収められる
        [UnityTest]
        public IEnumerator SetValueWithoutNotify_ChangesItems_AndClamps_WithoutChangeEvent()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var pagination = new Pagination { pageCount = 10, value = 4 };
            var events = new List<(int previous, int next)>();
            pagination.RegisterValueChangedCallback(evt => events.Add((evt.previousValue, evt.newValue)));
            root.Add(pagination);
            yield return null;

            pagination.SetValueWithoutNotify(6);
            yield return null;
            Assert.AreEqual(6, pagination.value);
            Assert.AreEqual("1 … 6 7 8 9 10", Sequence(pagination));

            pagination.SetValueWithoutNotify(-1);
            yield return null;
            Assert.AreEqual(0, pagination.value);

            pagination.SetValueWithoutNotify(10);
            yield return null;
            Assert.AreEqual(9, pagination.value);

            CollectionAssert.IsEmpty(events, "SetValueWithoutNotify must not fire a ChangeEvent");

            // 対照: callback が配線されていて、値が変わる set なら ChangeEvent が出る。value の set も範囲外を収め、収めた値を newValue に出す
            pagination.value = -1;
            yield return null;
            Assert.AreEqual(0, pagination.value);
            CollectionAssert.AreEqual(new[] { (9, 0) }, events, "a real change must fire a ChangeEvent");
        }

        // (F) pageCount を今の value より小さくすると value が最後のページに収まり、ChangeEvent は出ない
        [UnityTest]
        public IEnumerator PageCount_Shrink_ClampsValue_WithoutChangeEvent()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var pagination = new Pagination { pageCount = 10, value = 8 };
            var changes = 0;
            pagination.RegisterValueChangedCallback(evt => changes++);
            root.Add(pagination);
            yield return null;

            pagination.pageCount = 5;
            yield return null;

            Assert.AreEqual(4, pagination.value);
            Assert.AreEqual("1 2 3 4 5", Sequence(pagination));
            Assert.IsFalse(Next(pagination).enabledSelf);
            Assert.AreEqual(0, changes, "shrinking pageCount must not fire a ChangeEvent");

            // 増やす向き: value はそのまま残り、並びと次の Button が戻る。ChangeEvent は出ない
            pagination.pageCount = 10;
            yield return null;

            Assert.AreEqual(4, pagination.value);
            Assert.AreEqual("1 … 4 5 6 … 10", Sequence(pagination));
            Assert.IsTrue(Next(pagination).enabledSelf);
            Assert.AreEqual(0, changes, "growing pageCount must not fire a ChangeEvent");

            // 0 未満は 0 として扱い、value は 0 になる
            pagination.pageCount = -3;
            yield return null;

            Assert.AreEqual(0, pagination.pageCount);
            Assert.AreEqual(0, pagination.value);
            Assert.AreEqual("", Sequence(pagination));
            Assert.AreEqual(0, changes, "shrinking pageCount must not fire a ChangeEvent");

            // 対照: callback が配線されていて、値が変わる set なら ChangeEvent が出る
            pagination.pageCount = 5;
            pagination.value = 2;
            yield return null;
            Assert.AreEqual(1, changes, "a real change must fire a ChangeEvent");
        }

        // (A) (D) 今と同じ pageCount を入れても、items の子は同じインスタンスのまま。value も ChangeEvent も変わらない。
        // 0 未満を 0 に収めた値が同じ場合（pageCount 0 に -1）も作り直さない
        [UnityTest]
        public IEnumerator PageCount_SameValue_DoesNotRebuild()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var pagination = new Pagination { pageCount = 10, value = 4 };
            var changes = 0;
            pagination.RegisterValueChangedCallback(evt => changes++);
            root.Add(pagination);
            yield return null;

            var before = Items(pagination).Children().ToList();
            Assert.IsNotEmpty(before);

            pagination.pageCount = 10;
            yield return null;

            CollectionAssert.AreEqual(before, Items(pagination).Children().ToList(), "same pageCount must keep the same child instances");
            for (var i = 0; i < before.Count; i++)
                Assert.AreSame(before[i], Items(pagination)[i], $"child {i}");
            Assert.AreEqual(4, pagination.value);
            Assert.AreEqual(0, changes, "setting the same pageCount must not fire a ChangeEvent");

            // 対照: 違う値なら作り直す（子のインスタンスが変わる）
            pagination.pageCount = 5;
            yield return null;
            Assert.IsFalse(Items(pagination).Children().Any(child => before.Contains(child)), "a different pageCount must rebuild the children");

            // pageCount が 0 のとき、-1 は 0 に収まって同じ値なので作り直さない
            pagination.pageCount = 0;
            yield return null;
            Assert.AreEqual(0, pagination.pageCount);
            var empty = Items(pagination).Children().ToList();

            pagination.pageCount = -1;
            yield return null;

            Assert.AreEqual(0, pagination.pageCount);
            CollectionAssert.AreEqual(empty, Items(pagination).Children().ToList());
            Assert.AreEqual(0, changes, "setting the same pageCount must not fire a ChangeEvent");
        }

        // (B) 今と同じ pageCount を入れても、ページ番号の Button のフォーカスは外れない
        [UnityTest]
        public IEnumerator PageCount_SameValue_KeepsFocus()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var pagination = new Pagination { pageCount = 10, value = 4 };
            root.Add(pagination);
            yield return null;

            var button = PageButtons(pagination).First(b => b.text == "5");
            button.Focus();
            yield return null;
            Assert.AreSame(button, pagination.panel.focusController.focusedElement, "precondition: the page button must be focused");

            pagination.pageCount = 10;
            yield return null;

            Assert.AreSame(button, pagination.panel.focusController.focusedElement);
        }
    }
}
#endif
