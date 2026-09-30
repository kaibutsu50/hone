using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Gallery
{
    // Hone.Sandbox.UI を参照すると、namespace Hone の中の Button は Hone.Button を指す。ここの Button は Unity 標準のもの
    using Button = UnityEngine.UIElements.Button;

    // Hone.Button と、Core.uss の .hone-text と .hone-focusable を Gallery に載せる。素の "Label" "Button" の列と並べて、見た目の差を見る。
    // Register は sandbox 側から呼ぶ（registry/ 配下のコードには書かない）。呼ぶ時点は README の Gallery の節を参照。
    static class GalleryHoneEntries
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            // 最初の .hone-focusable に GalleryFocus が focus を当てるので、Hone.Button を先に登録して ring を写す。
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
        }
    }
}
