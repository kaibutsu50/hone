using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Gallery
{
    // 撮影用。Gallery の UI が読み込まれたあと、最初の .hone-focusable にフォーカスを当てて focus ring を写す。言語を切り替えたときも当て直す。
    // reload callback の中では当てず、schedule で後回しにする（GalleryController が同じ callback で列を作り終えてから探すため）。
    // 待ち時間は経験値で、これより短くて足りるかは確かめていない（読み込み時と言語の切り替え時の両方で、Build と Dialog の Open() がこの間に終わる前提）。
    // 対象が無い、またはフォーカスが移らなかったときは LogError を出す（ring が写っていない画像を黙って撮らないため）。
    [RequireComponent(typeof(PanelRenderer))]
    [RequireComponent(typeof(GalleryController))]
    public class GalleryFocus : MonoBehaviour
    {
        const long DelayMilliseconds = 200;

        void Awake()
        {
            GetComponent<PanelRenderer>().RegisterUIReloadCallback((panelRenderer, root, version) =>
            {
                root.schedule.Execute(() => FocusFirst(root)).ExecuteLater(DelayMilliseconds);

                // 言語を切り替えると GalleryController が Dialog を作り直し、各 Dialog が attach 時の Open() で自分の Cancel にフォーカスを移す。
                // 当て直さないと ring は最後に開いた Dialog の Cancel に残る。schedule で後回しにするのは、Build と Open() が終わってから当てるため。
                // dropdown が無いときは何もしない（エラーは GalleryController が出す）
                root.Q<DropdownField>("language")?.RegisterValueChangedCallback(_ =>
                    root.schedule.Execute(() => FocusFirst(root)).ExecuteLater(DelayMilliseconds));
            });
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
                // 待つ間に言語が切り替わると、target は作り直しで panel から外れる。次の FocusFirst が当て直すので、ここでは確かめない
                if (target.panel == null)
                    return;
                if (target.focusController?.focusedElement != target)
                    Debug.LogError("Gallery: the first '.hone-focusable' did not get focus", this);
            }).ExecuteLater(DelayMilliseconds);
        }
    }
}
