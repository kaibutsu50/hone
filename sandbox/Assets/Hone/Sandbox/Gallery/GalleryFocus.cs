using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Gallery
{
    // 撮影用。Gallery の UI が読み込まれたあと、最初の .hone-focusable にフォーカスを当てて focus ring を写す。
    // reload callback の中では当てず、schedule で後回しにする（GalleryController が同じ callback で列を作り終えてから探すため）。
    // 待ち時間は経験値で、これより短くて足りるかは確かめていない。
    // 対象が無い、またはフォーカスが移らなかったときは LogError を出す（ring が写っていない画像を黙って撮らないため）。
    [RequireComponent(typeof(PanelRenderer))]
    public class GalleryFocus : MonoBehaviour
    {
        const long DelayMilliseconds = 200;

        void Awake()
        {
            GetComponent<PanelRenderer>().RegisterUIReloadCallback((panelRenderer, root, version) =>
                root.schedule.Execute(() => FocusFirst(root)).ExecuteLater(DelayMilliseconds));
        }

        void FocusFirst(VisualElement root)
        {
            var target = root.Q(className: "hone-focusable");
            if (target == null)
            {
                Debug.LogError("Gallery: no element with class 'hone-focusable' to focus", this);
                return;
            }
            target.Focus();
            // フォーカス変更は非同期なので、確かめるのも後回しにする
            root.schedule.Execute(() =>
            {
                if (target.focusController?.focusedElement != target)
                    Debug.LogError("Gallery: the first '.hone-focusable' did not get focus", this);
            }).ExecuteLater(DelayMilliseconds);
        }
    }
}
