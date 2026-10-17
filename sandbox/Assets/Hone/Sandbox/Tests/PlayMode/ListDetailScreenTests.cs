#if UNITY_EDITOR
using System;
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
    // block は振る舞いを持たないので、確かめるのは次の 3 つ: UXML が読み込めて各要素が見つかること、Gallery の配線（GalleryListDetailScreen。block の README の「配線」と同じ手順）、block の USS が theme を通して当たっていること。
    // ToggleGroup と Pagination そのものの振る舞いは ToggleGroupTests と PaginationTests が担う。
    // 配線とレイアウトのテストは、Gallery が作った block を、高さの決まった容器（panel の root に当たる）へ移して測る。Gallery の行の中では、画面の高さが内容で決まるため。
    // コメントの (B)〜(H) は Issue #126 の受け入れ条件の番号。(A) の registry.json は cli/test/add.test.ts が確かめる
    public class ListDetailScreenTests
    {
        const string UxmlPath = "Assets/Hone/Blocks/ListDetailScreen/ListDetailScreen.uxml";
        const string PanelSettingsPath = "Assets/Hone/Sandbox/PanelSettings.asset";
        const string SandboxUxmlPath = "Assets/Hone/Sandbox/Sandbox.uxml";
        const string EntryName = "Hone.ListDetailScreen";
        const string ItemClass = "hone-list-detail-screen__item";
        const int MaxWaitFrames = 60;

        readonly List<Object> m_Created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in m_Created)
                Object.Destroy(obj);
            m_Created.Clear();
        }

        static IReadOnlyList<ScriptStrings> LoadTestStrings()
        {
            var path = Path.Combine(Application.dataPath, "Hone/Sandbox/Gallery/TestStrings.json");
            return GalleryController.ParseTestStrings(File.ReadAllText(path));
        }

        // PanelRenderer を 1 つ作り、UI が読み込まれたら root を返す。PanelSettings は複製して、panel とフォーカスの状態をテスト間で共有しない
        IEnumerator CreatePanel(Action<VisualElement> onReady)
        {
            var source = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            Assert.IsNotNull(source, $"{PanelSettingsPath} was not found");
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SandboxUxmlPath);
            Assert.IsNotNull(uxml, $"{SandboxUxmlPath} was not found");
            var panelSettings = Object.Instantiate(source);
            m_Created.Add(panelSettings);
            var go = new GameObject("ListDetailScreenTests");
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

        // Gallery で script の行を組み、index 番目（en の短文・長文、script の短文・長文の順）の block を、幅 width・高さ height の容器へ移して panel に置く
        IEnumerator CreateScreen(string script, int index, float width, float height, Action<VisualElement> onReady)
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var gallery = new VisualElement();
            GalleryController.Build(gallery, EntryName, GalleryController.Pick(LoadTestStrings(), script));
            var block = gallery.Query(className: "hone-list-detail-screen").ToList()[index];
            var container = new VisualElement();
            container.style.width = width;
            container.style.height = height;
            root.Add(container);
            block.RemoveFromHierarchy();
            container.Add(block);
            yield return WaitUntil(() => block.Q<Pagination>("pagination").worldBound.height > 0);
            onReady(block);
        }

        static IEnumerator WaitUntil(Func<bool> condition)
        {
            var frames = 0;
            while (!condition() && frames < MaxWaitFrames)
            {
                frames++;
                yield return null;
            }
            Assert.IsTrue(condition(), $"the condition was not met within {MaxWaitFrames} frames");
        }

        static void Submit(VisualElement target)
        {
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        static GalleryListDetailScreen Wiring(VisualElement block)
        {
            var wiring = block.userData as GalleryListDetailScreen;
            Assert.IsNotNull(wiring, "the block's userData is not a GalleryListDetailScreen");
            return wiring;
        }

        static List<Hone.Button> Rows(VisualElement block) => block.Q<ToggleGroup>("items").Query<Hone.Button>(className: ItemClass).ToList();

        // 選ばれている行の index（items の子の中）。無ければ空
        static List<int> SelectedRows(VisualElement block)
        {
            var items = block.Q<ToggleGroup>("items");
            var state = items.value;
            return Enumerable.Range(0, items.childCount).Where(i => state[i]).ToList();
        }

        static string DetailTitle(VisualElement block) => block.Q<Label>("detail-title").text;

        static List<string> DetailValues(VisualElement block) =>
            block.Q("detail-fields").Query<Label>(className: "hone-list-detail-screen__field-value").ToList().Select(l => l.text).ToList();

        // (B) UXML が読み込め、一覧、Pagination、詳細の Card、決定の Button、読み込み中と空の Label が見つかる
        [Test]
        public void Uxml_HasEachElement()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(tree, $"{UxmlPath} was not found");
            var screen = tree.Instantiate();

            var items = screen.Q<ToggleGroup>("items");
            Assert.IsNotNull(items);
            Assert.IsTrue(items.allowEmptySelection);
            Assert.AreEqual(5, items.Query<Hone.Button>(className: ItemClass).ToList().Count);
            Assert.IsNotNull(screen.Q<Pagination>("pagination"));
            var detail = screen.Q(className: "hone-list-detail-screen__detail");
            Assert.IsNotNull(detail);
            Assert.IsTrue(detail.ClassListContains("hone-card"));
            Assert.IsNotNull(detail.Q<Hone.Button>("confirm"));
            Assert.IsNotNull(screen.Q<Label>("loading"));
            Assert.IsNotNull(screen.Q<Label>("empty"));
        }

        // Gallery に登録されていて、英語と選んだ 1 言語の短文と長文で 4 つ並ぶ。各 block のタイトルと 1 行目の項目名にはその文字列が入る
        [Test]
        public void Gallery_BuildsOneScreenPerScriptAndKind()
        {
            CollectionAssert.Contains(GalleryController.Names, EntryName);
            var strings = GalleryController.Pick(LoadTestStrings(), "ja");
            var container = new VisualElement();

            GalleryController.Build(container, EntryName, strings);

            var screens = container.Query(className: "hone-list-detail-screen").ToList();
            Assert.AreEqual(4, screens.Count);
            var expected = strings.SelectMany(s => new[] { s.Short, s.Long }).ToList();
            CollectionAssert.AreEqual(expected, screens.Select(s => s.Q<Label>(className: "hone-list-detail-screen__title").text).ToList());
            CollectionAssert.AreEqual(expected, screens.Select(s => s.Q<Label>(className: "hone-list-detail-screen__item-label").text).ToList());
        }

        // (C) 12 件・1 ページ 5 件で pageCount が 3、ページ 0 / 1 / 2 の行の数が 5 / 5 / 2。行はそのページの項目（補足の #番号）
        [UnityTest]
        public IEnumerator Gallery_PageCountIsThreeAndRowsFollowThePage()
        {
            VisualElement block = null;
            yield return CreateScreen("ja", 2, 800, 480, b => block = b);
            var pagination = block.Q<Pagination>("pagination");

            Assert.AreEqual(12, Wiring(block).items.Count);
            Assert.AreEqual(3, pagination.pageCount);
            Assert.AreEqual(0, pagination.value);
            var expected = new[] { 5, 5, 2 };
            for (var page = 0; page < expected.Length; page++)
            {
                pagination.value = page;
                yield return null;
                var rows = Rows(block);
                Assert.AreEqual(expected[page], rows.Count, $"page {page}");
                var metas = rows.Select(r => r.Q<Label>(className: "hone-list-detail-screen__item-meta").text).ToList();
                CollectionAssert.AreEqual(Enumerable.Range(page * 5 + 1, expected[page]).Select(n => "#" + n).ToList(), metas, $"page {page}");
            }
        }

        // (D) ページ 0 の 2 行目を選んでからページ 1 へ送ると、どの行も選ばれておらず、詳細は 2 件目のまま。ページ 0 へ戻ると 2 行目が選ばれている
        [UnityTest]
        public IEnumerator Gallery_SelectionIsKeptAcrossPages()
        {
            VisualElement block = null;
            yield return CreateScreen("ja", 2, 800, 480, b => block = b);
            var wiring = Wiring(block);
            var pagination = block.Q<Pagination>("pagination");

            Submit(Rows(block)[1]);
            yield return null;
            Assert.AreEqual(1, wiring.selectedIndex);
            CollectionAssert.AreEqual(new[] { 1 }, SelectedRows(block));
            var second = wiring.items[1];

            pagination.value = 1;
            yield return null;
            CollectionAssert.IsEmpty(SelectedRows(block));
            Assert.AreEqual(1, wiring.selectedIndex);
            Assert.AreEqual(second.Label, DetailTitle(block));
            CollectionAssert.AreEqual(second.Values, DetailValues(block));

            pagination.value = 0;
            yield return null;
            CollectionAssert.AreEqual(new[] { 1 }, SelectedRows(block));
            Assert.AreEqual(1, wiring.selectedIndex);
            CollectionAssert.AreEqual(second.Values, DetailValues(block));
        }

        // (E) 未選択のとき confirm が disabled で、行を選ぶと enabled。選んだ行をもう一度押すと未選択に戻り、disabled になる。
        // Gallery は 1 件目を選んだ状態で出すので、その行を押して未選択から始める
        [UnityTest]
        public IEnumerator Gallery_ConfirmIsEnabledOnlyWhileAnItemIsSelected()
        {
            VisualElement block = null;
            yield return CreateScreen("ja", 2, 800, 480, b => block = b);
            var wiring = Wiring(block);
            var confirm = block.Q<Hone.Button>("confirm");
            var fields = block.Q("detail-fields");
            Assert.AreEqual(0, wiring.selectedIndex, "precondition: the Gallery selects the first item");
            Assert.IsTrue(confirm.enabledSelf);

            Submit(Rows(block)[0]);
            yield return null;
            Assert.AreEqual(-1, wiring.selectedIndex);
            CollectionAssert.IsEmpty(SelectedRows(block));
            Assert.IsFalse(confirm.enabledSelf);
            Assert.AreEqual("Select an item", DetailTitle(block));
            Assert.AreEqual(DisplayStyle.None, fields.resolvedStyle.display);

            Submit(Rows(block)[2]);
            yield return null;
            Assert.AreEqual(2, wiring.selectedIndex);
            Assert.IsTrue(confirm.enabledSelf);
            Assert.AreEqual(wiring.items[2].Label, DetailTitle(block));
            Assert.AreEqual(DisplayStyle.Flex, fields.resolvedStyle.display);

            Submit(Rows(block)[2]);
            yield return null;
            Assert.AreEqual(-1, wiring.selectedIndex);
            Assert.IsFalse(confirm.enabledSelf);
        }

        // (F) 高さの決まった親の中で、5 行のページと 2 行のページの Pagination の worldBound.y が同じ
        [UnityTest]
        public IEnumerator Gallery_PaginationStaysAtTheSameYOnAShortPage()
        {
            VisualElement block = null;
            yield return CreateScreen("ja", 2, 800, 480, b => block = b);
            var pagination = block.Q<Pagination>("pagination");
            Assert.AreEqual(5, Rows(block).Count);
            var y = pagination.worldBound.y;

            pagination.value = 2;
            yield return WaitUntil(() => Rows(block).Count == 2 && Rows(block).All(r => r.worldBound.height > 0));

            Assert.AreEqual(y, pagination.worldBound.y, 0.5f);
        }

        // (G) root の is-loading で行が隠れて読み込み中の Label が、is-empty で空の Label が出る。どちらも無いときは両方の Label が出ない
        [UnityTest]
        public IEnumerator Gallery_StateClassesSwitchTheListArea()
        {
            VisualElement block = null;
            yield return CreateScreen("ja", 2, 800, 480, b => block = b);
            var items = block.Q<ToggleGroup>("items");
            var loading = block.Q<Label>("loading");
            var empty = block.Q<Label>("empty");
            Assert.AreEqual(DisplayStyle.Flex, items.resolvedStyle.display);
            Assert.AreEqual(DisplayStyle.None, loading.resolvedStyle.display);
            Assert.AreEqual(DisplayStyle.None, empty.resolvedStyle.display);

            block.AddToClassList("is-loading");
            yield return WaitUntil(() => loading.resolvedStyle.display == DisplayStyle.Flex);
            Assert.AreEqual(DisplayStyle.None, items.resolvedStyle.display);
            Assert.AreEqual(DisplayStyle.None, empty.resolvedStyle.display);

            block.RemoveFromClassList("is-loading");
            block.AddToClassList("is-empty");
            yield return WaitUntil(() => empty.resolvedStyle.display == DisplayStyle.Flex);
            Assert.AreEqual(DisplayStyle.None, items.resolvedStyle.display);
            Assert.AreEqual(DisplayStyle.None, loading.resolvedStyle.display);

            block.RemoveFromClassList("is-empty");
            yield return WaitUntil(() => empty.resolvedStyle.display == DisplayStyle.None);
            Assert.AreEqual(DisplayStyle.Flex, items.resolvedStyle.display);
            Assert.AreEqual(DisplayStyle.None, loading.resolvedStyle.display);
        }

        // (H) 長いドイツ語と日本語の長文で、行の項目名が行の幅を超えず、省略記号で切る設定になる。補足も行の中に残る
        [UnityTest]
        public IEnumerator Gallery_LongGermanItemLabelStaysInsideTheRow() => LongItemLabelStaysInsideTheRow("de-long");

        [UnityTest]
        public IEnumerator Gallery_LongJapaneseItemLabelStaysInsideTheRow() => LongItemLabelStaysInsideTheRow("ja");

        IEnumerator LongItemLabelStaysInsideTheRow(string script)
        {
            VisualElement block = null;
            yield return CreateScreen(script, 3, 800, 480, b => block = b);
            var rows = Rows(block);
            Assert.AreEqual(5, rows.Count);
            yield return WaitUntil(() => rows.All(r => r.worldBound.width > 0));

            foreach (var row in rows)
            {
                var label = row.Q<Label>(className: "hone-list-detail-screen__item-label");
                var meta = row.Q<Label>(className: "hone-list-detail-screen__item-meta");
                Assert.AreEqual(TextOverflow.Ellipsis, label.resolvedStyle.textOverflow);
                Assert.AreEqual(WhiteSpace.NoWrap, label.resolvedStyle.whiteSpace);
                Assert.LessOrEqual(label.worldBound.xMax, row.worldBound.xMax + 1f, "the item label overflows the row");
                Assert.LessOrEqual(meta.worldBound.xMax, row.worldBound.xMax + 1f, "the item meta overflows the row");
                Assert.Less(label.worldBound.width, label.MeasureTextSize(label.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x,
                    "precondition: the text is longer than the row (otherwise no ellipsis is needed)");
            }
        }
    }
}
#endif
