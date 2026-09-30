using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Gallery
{
    // 撮影用。Gallery の UI が読み込まれたあと、最初の .hone-focusable にフォーカスを当てて focus ring を写す。
    // フォーカス変更は非同期で、GalleryController が列を作る callback との前後も決まっていないので、少し待ってから当てる。
    [RequireComponent(typeof(PanelRenderer))]
    public class GalleryFocus : MonoBehaviour
    {
        const long DelayMilliseconds = 200;

        void Awake()
        {
            GetComponent<PanelRenderer>().RegisterUIReloadCallback((panelRenderer, root, version) =>
                root.schedule.Execute(() => root.Q(className: "hone-focusable")?.Focus()).ExecuteLater(DelayMilliseconds));
        }
    }
}
