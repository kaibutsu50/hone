using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Gallery
{
    // 撮影用。Gallery の UI が読み込まれたあとと、RefocusLater を呼んだときに、カードの中（ScrollView、name `gallery`）の最初の .hone-focusable にフォーカスを当てて focus ring を写す。
    // 探す範囲をカードの中に絞るのは、左のメニューの Hone.Button と言語の Hone.Cycle も .hone-focusable を持ち、絞らないと ring がそちらに付くため。
    // reload callback の中では当てず、schedule で後回しにする（GalleryController が同じ callback で列を作り終えてから探すため）。
    // 待ち時間は経験値で、これより短くて足りるかは確かめていない（読み込み時と RefocusLater のどちらでも、Build と Dialog の Open() がこの間に終わる前提）。
    // 対象が無いときは何もしない（Label などフォーカスを持たないコンポーネントを選んでいることがある）。フォーカスが移らなかったときは LogError を出す（ring が写っていない画像を黙って撮らないため）。
    // コンポーネントや言語を選んだときは当て直さない（メニューを上下で選んでいる途中や、Cycle を左右で巡っている途中でフォーカスがカードへ飛び、続けて選べなくなる）。
    // 撮影では eval で SelectComponent と SelectScript のあとに RefocusLater を呼んで当てる。
    [RequireComponent(typeof(PanelRenderer))]
    [RequireComponent(typeof(GalleryController))]
    public class GalleryFocus : MonoBehaviour
    {
        const long DelayMilliseconds = 200;

        VisualElement m_Root;

        void Awake()
        {
            GetComponent<PanelRenderer>().RegisterUIReloadCallback((panelRenderer, root, version) =>
            {
                m_Root = root;
                root.schedule.Execute(() => FocusFirst(root)).ExecuteLater(DelayMilliseconds);
            });
        }

        // 撮影用。DelayMilliseconds 後に、カードの中の最初の .hone-focusable に当てる（読み込み時と同じ）
        public void RefocusLater()
        {
            if (m_Root == null || m_Root.panel == null)
                throw new System.InvalidOperationException("Gallery: the UI is not loaded yet");

            var root = m_Root;
            root.schedule.Execute(() => FocusFirst(root)).ExecuteLater(DelayMilliseconds);
        }

        void FocusFirst(VisualElement root)
        {
            // ScrollView が無いときは何もしない（エラーは GalleryController が出す）
            var target = root.Q<ScrollView>("gallery")?.contentContainer.Q(className: "hone-focusable");
            if (target == null)
                return;
            target.Focus();
            // フォーカス変更は非同期なので、確かめるのも後回しにする
            root.schedule.Execute(() =>
            {
                // 待つ間にコンポーネントか言語が切り替わると、target は作り直しで panel から外れる。当て直しは撮影側が RefocusLater で行うので、ここでは確かめない
                if (target.panel == null)
                    return;
                if (target.focusController?.focusedElement != target)
                    Debug.LogError("Gallery: the first '.hone-focusable' did not get focus", this);
            }).ExecuteLater(DelayMilliseconds);
        }
    }
}
