using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Gallery
{
    // Hone.Sandbox.UI を参照すると、namespace Hone の中の Button は Hone.Button を指す。ここの Button は Unity 標準のもの
    using Button = UnityEngine.UIElements.Button;

    // Hone.Button、Hone.Dialog、Hone.MessageWindow と、Core.uss の .hone-text と .hone-focusable を Gallery に載せる。左のリストで素の "Label" "Button" と選び比べて、見た目の差を見る。
    // Register は sandbox 側から呼ぶ（registry/ 配下のコードには書かない）。呼ぶ時点は README の Gallery の節を参照。
    static class GalleryHoneEntries
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            // 登録順の先頭がリストの初期選択になり、GalleryFocus がその最初の .hone-focusable に focus を当てるので、Hone.Button を先に登録して ring を写す。
            // 4 variant を 1 つのセルに縦に積む。セルは縦並び（align-items: stretch）で幅が決まるので、長文は省略記号になる
            GalleryController.Register("Hone.Button", text =>
            {
                var cell = new VisualElement();
                cell.AddToClassList("gallery-cell");
                foreach (Hone.Button.Variant variant in Enum.GetValues(typeof(Hone.Button.Variant)))
                    cell.Add(new Hone.Button { text = text, variant = variant });
                return cell;
            });
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
            });
            // 2 ページにして、1 ページ目で次のページがある状態（.is-waiting の ▼）を写す。charactersPerSecond は 0（即時表示）のまま。
            // MessageWindow は親の幅に従うので、セルの幅は Gallery.uss の .gallery-message-window で確定させる。
            // Show は Dialog の Open と揃えて attach の時点で呼ぶ（即時表示なので attach 前に呼んでも同じに出る）
            GalleryController.Register("Hone.MessageWindow", text =>
            {
                var window = new MessageWindow();
                window.AddToClassList("gallery-message-window");
                window.RegisterCallbackOnce<AttachToPanelEvent>(evt => window.Show(text, text));
                return window;
            });
        }
    }
}
