using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Gallery
{
    // 撮影用。Gallery の UI が読み込まれたあとと、RefocusLater を呼んだときに、カードの中（GalleryController.preview）の最初の .hone-focusable にフォーカスを当てて focus ring を写す。
    // 探す範囲をカードの中に絞るのは、左のメニューの Hone.Button と言語の Hone.Cycle も .hone-focusable を持ち、絞らないと ring がそちらに付くため。
    // reload callback の中では当てず、schedule で後回しにする（GalleryController が同じ callback で列を作り終えてから探すため）。
    // 待ち時間は経験値で、これより短くて足りるかは確かめていない（読み込み時と RefocusLater のどちらでも、Build と Dialog の Open() がこの間に終わる前提）。
    // 対象が無いときは何もしない（Hone.Switch などフォーカスを持たないコンポーネントを選んでいることがある）。フォーカスが移らなかったときは LogError を出す（ring が写っていない画像を黙って撮らないため）。
    // コンポーネントや言語を選んだときは当て直さない（メニューで Submit して選ぶたびにフォーカスがカードへ飛んで続けて上下で次の項目へ移れなくなり、Cycle を左右で巡っている途中でもカードへ飛ぶ）。
    // 切り替えるとカードの中が作り直され、フォーカスしていた要素は外れる。撮影では eval で SelectComponent と SelectScript のあとに RefocusLater を呼んで当てる。
    [RequireComponent(typeof(PanelRenderer))]
    [RequireComponent(typeof(GalleryController))]
    public class GalleryFocus : MonoBehaviour
    {
        const long DelayMilliseconds = 200;

        GalleryController m_Controller;
        VisualElement m_Root;

        void Awake()
        {
            m_Controller = GetComponent<GalleryController>();
            GetComponent<PanelRenderer>().RegisterUIReloadCallback((panelRenderer, root, version) =>
            {
                m_Root = root;
                root.schedule.Execute(FocusFirst).ExecuteLater(DelayMilliseconds);
            });
        }

        // 撮影用。DelayMilliseconds 後に、カードの中の最初の .hone-focusable に当てる（読み込み時と同じ）。
        // GalleryController の読み込みが済んでいない（または失敗した）ときは、SelectComponent と同じく InvalidOperationException を返す
        public void RefocusLater()
        {
            if (m_Root == null || m_Root.panel == null || m_Controller.preview == null)
                throw new System.InvalidOperationException("Gallery: the UI is not loaded yet");

            m_Root.schedule.Execute(FocusFirst).ExecuteLater(DelayMilliseconds);
        }

        void FocusFirst()
        {
            // preview が無いときは何もしない（読み込みに失敗したときのエラーは GalleryController が出す）
            var target = m_Controller.preview?.contentContainer.Q(className: "hone-focusable");
            if (target == null)
                return;
            target.Focus();
            // フォーカス変更は非同期なので、確かめるのも後回しにする
            m_Root.schedule.Execute(() =>
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
