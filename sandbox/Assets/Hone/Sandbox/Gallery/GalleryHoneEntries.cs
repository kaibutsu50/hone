using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Gallery
{
    // Core.uss の .hone-text と .hone-focusable を Gallery に載せる。素の "Label" "Button" の列と並べて、見た目の差を見る。
    // Register は sandbox 側から呼ぶ（registry/ 配下のコードには書かない）。呼ぶ時点は README の Gallery の節を参照。
    static class GalleryHoneEntries
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
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
