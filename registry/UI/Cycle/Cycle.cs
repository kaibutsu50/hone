using System.Collections.Generic;
using Hone.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hone
{
    // namespace Hone の中では、修飾しない Button は Hone.Button を指す。
    // 持つのは選択肢の文字列の列（choices）と選択中の index（value）だけ。選択肢ごとの値との対応は利用者が value から引く。
    // フォーカスは root だけが持ち、左右の NavigationMoveEvent を消費して value を動かす。前後の Button は focusable でなく、マウスとタッチ専用。
    // 3 つの子はコンストラクタで 1 回だけ作り、作り直さない。value か choices か wrap が変わったら、Label の text と前後の Button の enabled だけを更新する。
    [UxmlElement]
    public partial class Cycle : VisualElement, INotifyValueChanged<int>, INavigationMoveConsumer
    {
        readonly Button m_Previous;
        readonly Label m_Label;
        readonly Button m_Next;
        readonly List<string> m_Choices = new List<string>();
        bool m_Wrap;
        int m_Value;

        // choices は value より先に宣言する。UXML の属性は、UXML に書いた順ではなく宣言の順に適用される（Pagination で実測。6000.7.0b4）。
        // value が先の宣言だと、choices が空のまま 0 に収められてしまう

        // 選択肢。get は写しを返し、set は受け取った列を写して持つ。どちらの列を後から書き換えても Cycle には反映しない（変えるときは列を set し直す）。
        // 内部の列を外に出さないのは、value が範囲の外に残る書き換えと、get した列をそのまま set したときに Clear で空になるのを防ぐため。
        // null は空として扱う。value を範囲に収めるが、ChangeEvent は出さない
        [UxmlAttribute]
        public List<string> choices
        {
            get => new List<string>(m_Choices);
            set
            {
                m_Choices.Clear();
                if (value != null)
                    m_Choices.AddRange(value);
                SetValueWithoutNotify(m_Value);
            }
        }

        // true なら前後の操作で端から反対側へ巡る。value の set には効かない（範囲外は巡らず収める）
        [UxmlAttribute]
        public bool wrap
        {
            get => m_Wrap;
            set
            {
                m_Wrap = value;
                Refresh();
            }
        }

        // 選択中の index（0 始まり）。0〜max(choices.Count - 1, 0) に収め、変わったときだけ ChangeEvent<int> を 1 回出す
        [UxmlAttribute]
        public int value
        {
            get => m_Value;
            set
            {
                var clamped = Clamp(value);
                if (m_Value == clamped)
                    return;

                var previous = m_Value;
                SetValueWithoutNotify(clamped);
                using (var evt = ChangeEvent<int>.GetPooled(previous, clamped))
                {
                    evt.target = this;
                    SendEvent(evt);
                }
            }
        }

        public Cycle()
        {
            AddToClassList("hone-cycle");
            AddToClassList("hone-focusable");
            focusable = true;
            tabIndex = 0;

            m_Previous = new Button { text = "‹", variant = Button.Variant.Ghost, focusable = false };
            m_Previous.AddToClassList("hone-cycle__previous");
            m_Previous.clicked += () => Step(-1);
            Add(m_Previous);

            m_Label = new Label();
            m_Label.AddToClassList("hone-cycle__value");
            m_Label.AddToClassList("hone-text");
            Add(m_Label);

            m_Next = new Button { text = "›", variant = Button.Variant.Ghost, focusable = false };
            m_Next.AddToClassList("hone-cycle__next");
            m_Next.clicked += () => Step(1);
            Add(m_Next);

            // TrickleDown で受けるのは FocusScope と揃えるため。target が自分のときだけ処理する（前後の Button は focusable でないので target にならない）
            RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);

            Refresh();
        }

        // ChangeEvent を出さずに、範囲に収めた値にして Label と Button を更新する
        public void SetValueWithoutNotify(int newValue)
        {
            m_Value = Clamp(newValue);
            Refresh();
        }

        // 左右だけを消費する。FocusScope の trap はこの方向を自分で処理しない
        public bool ConsumesNavigationMove(NavigationMoveEvent.Direction direction) =>
            direction == NavigationMoveEvent.Direction.Left || direction == NavigationMoveEvent.Direction.Right;

        void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (evt.target != this || !ConsumesNavigationMove(evt.direction))
                return;

            // 端で動かないときも止める。止めないと、端でだけ既定のナビが左右の隣の要素へフォーカスを移す
            panel.focusController.IgnoreEvent(evt);
            Step(evt.direction == NavigationMoveEvent.Direction.Left ? -1 : 1);
        }

        // 前後の操作。wrap なら端から反対側へ巡り、そうでなければ端で止まる（value の set が収める）
        void Step(int delta)
        {
            var count = m_Choices.Count;
            if (count <= 1)
                return;

            var next = m_Value + delta;
            if (m_Wrap)
                next = (next % count + count) % count;
            value = next;
        }

        int Clamp(int index) => Mathf.Clamp(index, 0, Mathf.Max(m_Choices.Count - 1, 0));

        void Refresh()
        {
            var count = m_Choices.Count;
            m_Label.text = count > 0 ? m_Choices[m_Value] : "";
            m_Previous.SetEnabled(count > 1 && (m_Wrap || m_Value > 0));
            m_Next.SetEnabled(count > 1 && (m_Wrap || m_Value < count - 1));
        }
    }
}
