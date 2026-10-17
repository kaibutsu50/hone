using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Gallery
{
    // Hone.Button、Hone.Dialog、Hone.MessageWindow、Hone.Switch、Hone.Toggle、Hone.ToggleGroup、Hone.Pagination、Hone.Cycle、Hone.Tabs、見た目だけの Hone.Card と Hone.Separator と Hone.ScrollArea（Gallery の名前だけで、C# のクラスは無い）と、block の SettingsScreen と ListDetailScreen（Gallery の名前は Hone.SettingsScreen と Hone.ListDetailScreen。block の C# のクラスは無い。ListDetailScreen の配線は sandbox の GalleryListDetailScreen）を Gallery に載せる。
    // Register は sandbox 側から呼ぶ（registry/ 配下のコードには書かない）。呼ぶ時点は README の Gallery の節を参照。
    static class GalleryHoneEntries
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            // 登録順の先頭がメニューの初期選択になり、GalleryFocus がその最初の .hone-focusable に focus を当てるので、Hone.Button を先に登録して ring を写す。
            // 全 variant を 1 つのセルに縦に積む。セルは縦並び（align-items: stretch）で幅が決まるので、長文は省略記号になる
            GalleryController.Register("Hone.Button", text =>
            {
                var cell = new VisualElement();
                cell.AddToClassList("gallery-cell");
                foreach (Hone.Button.Variant variant in Enum.GetValues(typeof(Hone.Button.Variant)))
                    cell.Add(new Hone.Button { text = text, variant = variant });
                return cell;
            }, Describe("Button", string.Join(" / ", Enum.GetNames(typeof(Hone.Button.Variant)))));
            // 開いた状態の Hone.Dialog。文字列はタイトルと本文に入れ、ボタンは短い固定文字列にする（Button は縮まず折り返さないため）。
            // Dialog は親の全面を覆う absolute なので、セルの中では relative にして内容の高さで並べる。
            // Open() は panel に attach されてから呼ぶ（attach 前だと初期フォーカスが当たらない）。
            // 開くたびにその Dialog の Cancel へフォーカスが移り、全セルの Dialog が BackStack に積まれる（Cancel を押すと 1 つずつ閉じる）。
            // 撮影の ring は GalleryFocus が後から最初の .hone-focusable に当て直す
            GalleryController.Register("Hone.Dialog", text =>
            {
                var dialog = new Dialog();
                dialog.AddToClassList("gallery-dialog");
                dialog.style.position = Position.Relative;
                var title = new Label(text);
                title.AddToClassList("hone-dialog__title");
                title.AddToClassList("hone-text");
                dialog.Add(title);
                var description = new Label(text);
                description.AddToClassList("hone-dialog__description");
                description.AddToClassList("hone-text");
                dialog.Add(description);
                var footer = new VisualElement();
                footer.AddToClassList("hone-dialog__footer");
                footer.Add(new Hone.Button { text = "Cancel", variant = Hone.Button.Variant.Outline });
                footer.Add(new Hone.Button { text = "OK" });
                dialog.Add(footer);
                dialog.RegisterCallbackOnce<AttachToPanelEvent>(evt => dialog.Open());
                return dialog;
            }, Describe("Dialog", "hone-dialog--blur (opt-in, Screen Space only)"));
            // 2 ページにして、1 ページ目で次のページがある状態（.is-waiting の ▼）を写す。charactersPerSecond は 0（即時表示）のまま。
            // MessageWindow は親の幅に従うので、セルの幅は Gallery.uss の .gallery-message-window で確定させる。
            // Show は Dialog の Open と揃えて attach の時点で呼ぶ（即時表示なので attach 前に呼んでも同じに出る）。
            // 1 ページ目は話者を text を 3 回つないだものにして名札を写す（長文のセルでは幅を超え、省略記号で切れる）。2 ページ目は話者なし
            GalleryController.Register("Hone.MessageWindow", text =>
            {
                var window = new MessageWindow();
                window.AddToClassList("gallery-message-window");
                var speaker = text + " " + text + " " + text;
                window.RegisterCallbackOnce<AttachToPanelEvent>(evt => window.Show(new MessageWindow.Page(speaker, text), new MessageWindow.Page(null, text)));
                return window;
            }, Describe("MessageWindow"));
            // off と on を 1 つのセルに縦に積む。文言は label に入れる（text は track の中に入る）。長文の言語では label が省略記号で切れ、track は右端に残る。
            // hone-focusable は付けていないので ring は写らない（Switch の ring は track の border。GalleryFocus の当て先にもならない）
            GalleryController.Register("Hone.Switch", text =>
            {
                var cell = new VisualElement();
                cell.AddToClassList("gallery-cell");
                cell.Add(new Switch { label = text });
                cell.Add(new Switch { label = text, value = true });
                return cell;
            }, Describe("Switch"));
            // off と on を 1 つのセルに縦に積む。セルは縦並びで幅が決まるので、幅を超える長文は省略記号になる。
            // Unity 標準の Toggle と紛らわしいので Hone.Toggle と完全修飾で書く。hone-focusable を持つので、撮影時に GalleryFocus.RefocusLater で en の行の off の Toggle に当て直すと ring が写る。コンポーネントを選んだだけでは当たらない
            GalleryController.Register("Hone.Toggle", text =>
            {
                var cell = new VisualElement();
                cell.AddToClassList("gallery-cell");
                cell.Add(new Hone.Toggle { text = text });
                cell.Add(new Hone.Toggle { text = text, value = true });
                return cell;
            }, Describe("Toggle"));
            // 3 つの Hone.Button を並べた ToggleGroup を、幅を決めるためにセルに入れる（セルは縦並びで、行の残りを短文と長文で等分する）。attach の時点で 2 番目（index 1）を選んだ状態にする。項目は flex-grow: 1 で均等に広がり、長文の言語では縮んで省略記号になる。
            // allowEmptySelection が false（既定）だと、最初の子を足した時点で基底が index 0 を選ぶので、全部外してから 1 を選ぶ
            GalleryController.Register("Hone.ToggleGroup", text =>
            {
                var group = new ToggleGroup();
                for (var i = 0; i < 3; i++)
                    group.Add(new Hone.Button { text = text });
                group.RegisterCallbackOnce<AttachToPanelEvent>(evt =>
                {
                    var state = group.value;
                    for (var i = 0; i < state.length; i++)
                        state[i] = false;
                    state[1] = true;
                    group.SetValueWithoutNotify(state);
                });
                var cell = new VisualElement();
                cell.AddToClassList("gallery-cell");
                cell.Add(group);
                return cell;
            }, Describe("ToggleGroup"));
            // 10 ページの 5 ページ目（value 4）。両側に省略記号が出る並び（1 … 4 [5] 6 … 10）を写す。
            // 数字と記号だけなので文字列は使わず、言語を切り替えても同じに写る。pageCount を先に設定する（value は pageCount の範囲に収められる）。
            // 前の Button が hone-focusable を持つので、Toggle と同じく RefocusLater で en の行の 1 つ目の前の Button に ring が写る
            GalleryController.Register("Hone.Pagination", text => new Pagination { pageCount = 10, value = 4 }, Describe("Pagination"));
            // 途中（value 1。前後とも有効）と最初（value 0。前の Button が disabled）を 1 つのセルに縦に積む。choices の 3 つは同じ文字列にする（写すのは中央の文字列と前後の Button の状態だけ）。
            // 中央の文字列は収まらなければ省略記号で切れ、前後の Button は縮まない（en と Japanese の長文は、Screen Space と World Space のどちらでも切れずに入った。6000.7.0b4。メニューとカードの構成にする前の、セルが広いレイアウトで確かめた）。choices を value より先に設定する（value は choices の範囲に収められる）。
            // root が hone-focusable を持つので、Toggle と同じく RefocusLater で en の行の 1 つ目の root に ring が写る
            GalleryController.Register("Hone.Cycle", text =>
            {
                var cell = new VisualElement();
                cell.AddToClassList("gallery-cell");
                cell.Add(new Cycle { choices = new List<string> { text, text, text }, value = 1 });
                cell.Add(new Cycle { choices = new List<string> { text, text, text }, value = 0 });
                return cell;
            }, Describe("Cycle"));
            // 3 つのタブのラベルを text にし、2 番目（value 1）を選んだ状態にする。タブは ToggleGroup と同じく均等に広がり、長文の言語では縮んで省略記号になる。
            // 中身は文字列の Label と短い固定文字列の Button（Button は縮まず折り返さないため）。表示されるのは 2 番目の中身だけ。
            // tabs を value より先に設定する（value は tabs の範囲に収められる）。中身は attach 前に足す（表示は attach の時点で揃う）。
            // タブの Button が hone-focusable を持つので、Toggle と同じく RefocusLater で en の行の 1 つ目のタブに ring が写る
            GalleryController.Register("Hone.Tabs", text =>
            {
                var tabs = new Tabs { tabs = new List<string> { text, text, text }, value = 1 };
                for (var i = 0; i < 3; i++)
                {
                    var content = new VisualElement();
                    var label = new Label(text);
                    label.AddToClassList("hone-text");
                    content.Add(label);
                    content.Add(new Hone.Button { text = "Save " + (i + 1) });
                    tabs.Add(content);
                }
                var cell = new VisualElement();
                cell.AddToClassList("gallery-cell");
                cell.Add(tabs);
                return cell;
            }, Describe("Tabs"));
            // 見た目だけの部品（.cs を持たない）。状態のデモは無いので、Default と Compact を 1 つのセルに縦に積む。
            // 見出し・説明・中身に text を入れ、フッターは短い固定文字列にする（長文の言語では見出しと説明と中身が折り返す）
            GalleryController.Register("Hone.Card", text =>
            {
                var cell = new VisualElement();
                cell.AddToClassList("gallery-cell");
                cell.Add(CreateCard(text, false));
                cell.Add(CreateCard(text, true));
                return cell;
            }, Describe("Card", "Default / hone-card--compact"));
            // 見た目だけの部品（.cs を持たない）。状態のデモは無いので、横線を text の Label の間に、縦線を短い固定文字列の Label の間に置いて 1 つのセルに縦に積む。
            // Label はすべて幅に収まらなければ折り返す（既定の Label は折り返さない）。縦線の行は短い固定文字列なので、実際には折り返さず、言語を切り替えても変わらない
            GalleryController.Register("Hone.Separator", text =>
            {
                var cell = new VisualElement();
                cell.AddToClassList("gallery-cell");
                cell.Add(CreateSeparatorLabel(text, false));
                var horizontal = new VisualElement();
                horizontal.AddToClassList("hone-separator");
                cell.Add(horizontal);
                cell.Add(CreateSeparatorLabel(text, false));
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                var labels = new[] { "Blog", "Docs", "Source" };
                for (var i = 0; i < labels.Length; i++)
                {
                    if (i > 0)
                    {
                        var vertical = new VisualElement();
                        vertical.AddToClassList("hone-separator");
                        vertical.AddToClassList("hone-separator--vertical");
                        row.Add(vertical);
                    }
                    row.Add(CreateSeparatorLabel(labels[i], true));
                }
                cell.Add(row);
                return cell;
            }, Describe("Separator", "Horizontal / hone-separator--vertical"));
            // 見た目だけの部品（.cs を持たない）。状態のデモは無いので、高さを固定した ScrollView に text の Label を 6 つ入れ、縦のスクロールバーが出る状態で 1 つのセルに置く。
            // 横のスクロールバーは整えない部品なので Hidden にする。Label は幅に収まらなければ折り返す（既定の Label は折り返さない）
            GalleryController.Register("Hone.ScrollArea", text =>
            {
                var cell = new VisualElement();
                cell.AddToClassList("gallery-cell");
                var scrollView = new ScrollView(ScrollViewMode.Vertical);
                scrollView.AddToClassList("hone-scroll-area");
                scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                scrollView.style.height = 160;
                for (var i = 0; i < 6; i++)
                {
                    var label = new Label(text);
                    label.AddToClassList("hone-text");
                    label.style.whiteSpace = WhiteSpace.Normal;
                    scrollView.Add(label);
                }
                cell.Add(scrollView);
                return cell;
            }, Describe("ScrollArea"));
            // block。SettingsScreen.uxml を読み込み、各行の見出しと説明、確認の Dialog のタイトルと本文に text を入れる（長文の言語では説明が折り返す）。
            // 操作は UXML のまま使い、Cycle の選択肢と ToggleGroup のラベルだけ text にする（収まらなければ省略記号になる）。Button の文言（Refresh / Delete、Dialog の Cancel / Delete）は短い固定文字列のまま（行の中で max-width に切られた Button の長いラベルは確かめていない）。
            // Destructive の行の Button で確認の Dialog を開き、Cancel / 確定で閉じる（block は振る舞いを持たないので、配線はここでする）。Dialog は閉じた状態で並ぶ。
            // UXML は Editor でだけ読む（AssetDatabase は Editor 専用）。Player では代わりの案内の Label を返す。
            // 説明（variant の一覧と UXML の使用例）は渡さない。UXML が長く、列の先頭に出すと SettingsScreen が表示領域の外へ押し下げられる（使用例は README にある）。
            // 初期選択（先頭の登録）は Hone.Button のままにするため、先頭には登録しない
            GalleryController.Register("Hone.SettingsScreen", CreateSettingsScreen, null);
            // block。ListDetailScreen.uxml を読み込み、画面のタイトルに text を入れ、12 件・1 ページ 5 件のサンプルのデータを GalleryListDetailScreen で配線する（block は振る舞いを持たないので、配線はここでする）。
            // 各項目の項目名と詳細の説明は text、補足は "#<番号>"。詳細の項目の行の名前（Category / Size / Updated）と値、決定の Button の文言は短い固定文字列のまま。
            // 最初の項目を選んだ状態で出す（Screen Space の長文の言語では、行の項目名が省略記号になり、詳細の見出しと説明が折り返す）。読み込み中と空は、block の root の userData の GalleryListDetailScreen から SetLoading / SetItems で切り替える。
            // 一覧の列（40%）に Pagination の前後とページ番号の 5 つの Button が収まるよう、Gallery.uss で短文と長文を縦に積む。
            // UXML は Editor でだけ読む（SettingsScreen と同じ）。説明は渡さない（SettingsScreen と同じく UXML が長い。使用例は README にある）
            GalleryController.Register("Hone.ListDetailScreen", CreateListDetailScreen, null);
        }

        static Label CreateSeparatorLabel(string text, bool margin)
        {
            var label = new Label(text);
            label.AddToClassList("hone-text");
            label.style.whiteSpace = WhiteSpace.Normal;
            if (margin)
            {
                label.style.marginLeft = 12;
                label.style.marginRight = 12;
            }
            return label;
        }

        static VisualElement CreateCard(string text, bool compact)
        {
            var card = new VisualElement();
            card.AddToClassList("hone-card");
            if (compact)
                card.AddToClassList("hone-card--compact");
            card.Add(CreateCardPart("hone-card__header", CreateCardLabel("hone-card__title", text), CreateCardLabel("hone-card__description", text)));
            card.Add(CreateCardPart("hone-card__content", CreateCardLabel(null, text)));
            card.Add(CreateCardPart("hone-card__footer", CreateCardLabel(null, "Footer")));
            return card;
        }

        static VisualElement CreateCardPart(string className, params VisualElement[] children)
        {
            var part = new VisualElement();
            part.AddToClassList(className);
            foreach (var child in children)
                part.Add(child);
            return part;
        }

        static Label CreateCardLabel(string className, string text)
        {
            var label = new Label(text);
            if (className != null)
                label.AddToClassList(className);
            label.AddToClassList("hone-text");
            return label;
        }

        const string SettingsScreenPath = "Assets/Hone/Blocks/SettingsScreen/SettingsScreen.uxml";

        static VisualElement CreateSettingsScreen(string text)
        {
#if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SettingsScreenPath);
            if (asset == null)
            {
                Debug.LogWarning($"Gallery: {SettingsScreenPath} was not found; SettingsScreen is shown as a placeholder");
                return new Label($"{SettingsScreenPath} was not found");
            }
            var screen = asset.Instantiate();
            screen.AddToClassList("gallery-cell");
            foreach (var label in screen.Query<Label>(className: "hone-settings-screen__heading").ToList())
                label.text = text;
            foreach (var label in screen.Query<Label>(className: "hone-settings-screen__description").ToList())
                label.text = text;
            foreach (var label in screen.Query<Label>(className: "hone-dialog__title").ToList())
                label.text = text;
            foreach (var label in screen.Query<Label>(className: "hone-dialog__description").ToList())
                label.text = text;
            screen.Q<Cycle>("quality").choices = new List<string> { text, text, text };
            foreach (var button in screen.Q<ToggleGroup>("density").Query<Hone.Button>().ToList())
                button.text = text;
            var dialog = screen.Q<Dialog>("confirm-delete");
            screen.Q<Hone.Button>("delete").clicked += dialog.Open;
            screen.Q<Hone.Button>("cancel").clicked += dialog.Close;
            screen.Q<Hone.Button>("confirm").clicked += dialog.Close;
            return screen;
#else
            return new Label("SettingsScreen is shown only in the Editor");
#endif
        }

        const string ListDetailScreenPath = "Assets/Hone/Blocks/ListDetailScreen/ListDetailScreen.uxml";
        const int ListDetailScreenItemCount = 12;

        static VisualElement CreateListDetailScreen(string text)
        {
#if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ListDetailScreenPath);
            if (asset == null)
            {
                Debug.LogWarning($"Gallery: {ListDetailScreenPath} was not found; ListDetailScreen is shown as a placeholder");
                return new Label($"{ListDetailScreenPath} was not found");
            }
            var screen = asset.Instantiate();
            screen.AddToClassList("gallery-list-detail-screen");
            var block = screen.Q(className: "hone-list-detail-screen");
            block.Q<Label>(className: "hone-list-detail-screen__title").text = text;
            var categories = new[] { "General", "Tools", "Media" };
            var items = new List<ListDetailItem>();
            for (var i = 0; i < ListDetailScreenItemCount; i++)
            {
                var number = i + 1;
                items.Add(new ListDetailItem(text, "#" + number, text, new[] { categories[i % categories.Length], number * 3 + " MB", "Day " + number }));
            }
            var wiring = new GalleryListDetailScreen(block);
            wiring.SetItems(items, 0);
            block.userData = wiring;
            return screen;
#else
            return new Label("ListDetailScreen is shown only in the Editor");
#endif
        }

        // Hone/UI/<component>/<component>.uxml を読んで説明にする。Editor 専用（Player には Assets/ のファイルが無く、説明なしになる）。
        // Editor でファイルが無いとき（component の誤りやリネーム）は、説明なしにして警告を出す。
        // variants は variant（variant プロパティの値と、README の opt-in の variant クラス）を持つ部品だけ渡す（無ければ null で、Gallery は「なし」と出す）
        static ComponentDescription? Describe(string component, string variants = null)
        {
            var path = Path.Combine(Application.dataPath, "Hone/UI", component, component + ".uxml");
            if (!File.Exists(path))
            {
                if (Application.isEditor)
                    Debug.LogWarning($"Gallery: {path} was not found; {component} is shown without a description");
                return null;
            }
            return new ComponentDescription(variants, File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd());
        }
    }
}
