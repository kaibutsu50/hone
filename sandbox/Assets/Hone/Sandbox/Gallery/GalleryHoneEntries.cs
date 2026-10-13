using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Gallery
{
    // Hone.Sandbox.UI を参照すると、namespace Hone の中の Button は Hone.Button を指す。ここの Button は Unity 標準のもの
    using Button = UnityEngine.UIElements.Button;

    // Hone.Button、Hone.Dialog、Hone.MessageWindow、Hone.Switch、Hone.Toggle、Hone.ToggleGroup、Hone.Pagination、Hone.Cycle と、Core.uss の .hone-text と .hone-focusable を Gallery に載せる。左のメニューで素の "Label" "Button" と選び比べて、見た目の差を見る。
    // Register は sandbox 側から呼ぶ（registry/ 配下のコードには書かない）。呼ぶ時点は README の Gallery の節を参照。
    static class GalleryHoneEntries
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            // 登録順の先頭がメニューの初期選択になり、GalleryFocus がその最初の .hone-focusable に focus を当てるので、Hone.Button を先に登録して ring を写す。
            // 4 variant を 1 つのセルに縦に積む。セルは縦並び（align-items: stretch）で幅が決まるので、長文は省略記号になる
            GalleryController.Register("Hone.Button", text =>
            {
                var cell = new VisualElement();
                cell.AddToClassList("gallery-cell");
                foreach (Hone.Button.Variant variant in Enum.GetValues(typeof(Hone.Button.Variant)))
                    cell.Add(new Hone.Button { text = text, variant = variant });
                return cell;
            }, Describe("Button", string.Join(" / ", Enum.GetNames(typeof(Hone.Button.Variant)))));
            GalleryController.Register("Label.hone-text", text =>
            {
                var label = new Label(text);
                label.AddToClassList("hone-text");
                return label;
            });
            GalleryController.Register("Button.hone-focusable", text =>
            {
                var button = new Button { text = text };
                button.AddToClassList("hone-focusable");
                return button;
            });
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
            // Unity 標準の Toggle と紛らわしいので Hone.Toggle と完全修飾で書く。hone-focusable を持つので、撮影時に GalleryFocus.RefocusLaterで en の行の off の Toggle に当て直すと ring が写る。コンポーネントを選んだだけでは当たらない
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
            // 前の Button が hone-focusable を持つので、Toggle と同じく RefocusLaterで en の行の 1 つ目の前の Button に ring が写る
            GalleryController.Register("Hone.Pagination", text => new Pagination { pageCount = 10, value = 4 }, Describe("Pagination"));
            // 途中（value 1。前後とも有効）と最初（value 0。前の Button が disabled）を 1 つのセルに縦に積む。choices の 3 つは同じ文字列にする（写すのは中央の文字列と前後の Button の状態だけ）。
            // 中央の文字列は収まらなければ省略記号で切れ、前後の Button は縮まない（en と Japanese の長文は、Screen Space と World Space のどちらでも切れずに入った。6000.7.0b4）。choices を value より先に設定する（value は choices の範囲に収められる）。
            // root が hone-focusable を持つので、Toggle と同じく RefocusLaterで en の行の 1 つ目の root に ring が写る
            GalleryController.Register("Hone.Cycle", text =>
            {
                var cell = new VisualElement();
                cell.AddToClassList("gallery-cell");
                cell.Add(new Cycle { choices = new List<string> { text, text, text }, value = 1 });
                cell.Add(new Cycle { choices = new List<string> { text, text, text }, value = 0 });
                return cell;
            }, Describe("Cycle"));
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
