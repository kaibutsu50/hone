using System;
using Hone.Core;
using UnityEngine.UIElements;

namespace Hone
{
    // フォーカスの trap・初期フォーカス・復元は FocusScope に、Cancel で閉じるのは BackStack に委ねる。
    // Dialog 自身は NavigationMoveEvent / NavigationCancelEvent / FocusOutEvent を扱わない。
    // 扱うのは overlay の PointerDown だけで、閉じる時にその既定のフォーカス処理を止める。
    //
    // 構造: Dialog（.hone-dialog）> overlay（.hone-dialog__overlay）+ FocusScope（.hone-dialog__content）。
    // UXML の子要素と Add() は contentContainer（FocusScope）に入る。
    [UxmlElement]
    public partial class Dialog : VisualElement, IDismissable
    {
        // true のとき、overlay を押すと閉じる。false でも overlay は表示され、背後への入力を止める。変わるのは押して閉じるかだけ
        [UxmlAttribute]
        public bool modal { get; set; } = true;

        // true のとき、開いている間 BackStack に積み、Cancel（ゲームパッドの B、Esc）で閉じる。
        // 積むのは Open() と attach の時点なので、開いている間に変えても次に開くまで反映されない
        [UxmlAttribute]
        public bool dismissOnCancel { get; set; } = true;

        public bool isOpen { get; private set; }

        public event Action opened;
        public event Action closed;

        readonly VisualElement m_Overlay;
        readonly FocusScope m_Content;

        // Open() か attach で積んだ BackStack。閉じる時と panel から外れる時に、ここから外す
        BackStack m_Stack;

        public override VisualElement contentContainer => m_Content;

        public Dialog()
        {
            AddToClassList("hone-dialog");
            // 閉じている状態が既定。USS が読まれていなくても隠れるよう、inline で指定する
            style.display = DisplayStyle.None;

            m_Overlay = new VisualElement();
            m_Overlay.AddToClassList("hone-dialog__overlay");
            m_Overlay.RegisterCallback<PointerDownEvent>(OnOverlayPointerDown);

            // autoFocus は false にする。true だと、閉じた（display: none の）Dialog が panel に attach された時点で
            // FocusScope が Activate() し、中の要素にフォーカスを移してしまう（canGrabFocus は祖先の display を見ない）。
            // 初期フォーカスは Open() から FocusFirst() で当てる
            m_Content = new FocusScope { trap = true, autoFocus = false };
            m_Content.AddToClassList("hone-dialog__content");

            // contentContainer を差し替えているので、自分の構造は hierarchy に直接足す
            hierarchy.Add(m_Overlay);
            hierarchy.Add(m_Content);

            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        // 開いている間は何もしない。panel に attach されていないときは、attach された時点で BackStack に積む
        public void Open()
        {
            if (isOpen)
                return;
            isOpen = true;
            style.display = DisplayStyle.Flex;
            // Activate() が今のフォーカスを「閉じた時に戻す先」として記憶する。フォーカスを移す前に呼ぶ
            m_Content.Activate();
            m_Content.FocusFirst();
            PushToStack();
            opened?.Invoke();
        }

        // 閉じている間は何もしない（IDismissable の冪等性）
        public void Close()
        {
            if (!isOpen)
                return;
            isOpen = false;
            RemoveFromStack();
            m_Content.Deactivate();
            style.display = DisplayStyle.None;
            closed?.Invoke();
        }

        void IDismissable.Dismiss() => Close();

        void PushToStack()
        {
            if (!dismissOnCancel || panel == null)
                return;
            m_Stack = BackStack.For(panel);
            m_Stack.Push(this);
        }

        void RemoveFromStack()
        {
            m_Stack?.Remove(this);
            m_Stack = null;
        }

        // PointerDown の既定の処理（押した要素が focusable でなければフォーカスを外す）は、この callback の後に走る。
        // 閉じる時は IgnoreEvent で止め、Close() で戻したフォーカスを外させない
        void OnOverlayPointerDown(PointerDownEvent evt)
        {
            if (!modal)
                return;
            panel.focusController.IgnoreEvent(evt);
            Close();
        }

        // attach 前に Open() した場合と、開いたまま付け直した場合に、Cancel で閉じられるよう積む
        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (isOpen)
                PushToStack();
        }

        // 開いたまま panel から外れても BackStack に残さない（残ると以後の Cancel が詰まる）。開閉の状態は変えない
        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            RemoveFromStack();
        }
    }
}
