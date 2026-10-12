using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hone
{
    // namespace Hone の中では、修飾しない Button は Hone.Button を指す。
    // 持つのは今のページ（value、0 始まり）とページ数（pageCount）だけで、ページごとの中身は持たない。
    // 前後の Button はコンストラクタで 1 回だけ作り、作り直さない（作り直すと、押した直後にフォーカスが外れる）。
    // ページ番号と省略記号は、value か pageCount が変わるたびに作り直す。作り直した後にフォーカスを戻す処理は持たない。
    [UxmlElement]
    public partial class Pagination : VisualElement, INotifyValueChanged<int>
    {
        const int Ellipsis = -1;

        readonly Button m_Previous;
        readonly VisualElement m_Items;
        readonly Button m_Next;
        int m_PageCount = 1;
        int m_Value;

        // value より先に宣言する。UXML の属性は、UXML に書いた順ではなく宣言の順に適用される（UXML に value を先に書いても 10 / 4 が残った。6000.7.0b4）。
        // value が先の宣言だと、既定の 1 ページに収められてしまう
        // 0 未満は 0 として扱う。変えると value を範囲に収めるが、ChangeEvent は出さない。
        // 収めた値が今と同じなら何もしない（作り直すとページ番号の Button のフォーカスが外れる）
        [UxmlAttribute]
        public int pageCount
        {
            get => m_PageCount;
            set
            {
                var clamped = Mathf.Max(value, 0);
                if (m_PageCount == clamped)
                    return;

                m_PageCount = clamped;
                SetValueWithoutNotify(m_Value);
            }
        }

        // 今のページ（0 始まり）。0〜max(pageCount - 1, 0) に収め、変わったときだけ ChangeEvent<int> を 1 回出す
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

        public Pagination()
        {
            AddToClassList("hone-pagination");

            m_Previous = new Button { text = "‹", variant = Button.Variant.Ghost };
            m_Previous.AddToClassList("hone-pagination__previous");
            m_Previous.clicked += () => value = m_Value - 1;
            Add(m_Previous);

            m_Items = new VisualElement();
            m_Items.AddToClassList("hone-pagination__items");
            Add(m_Items);

            m_Next = new Button { text = "›", variant = Button.Variant.Ghost };
            m_Next.AddToClassList("hone-pagination__next");
            m_Next.clicked += () => value = m_Value + 1;
            Add(m_Next);

            Rebuild();
        }

        // ChangeEvent を出さずに、範囲に収めた値にしてページ番号を作り直す
        public void SetValueWithoutNotify(int newValue)
        {
            m_Value = Clamp(newValue);
            Rebuild();
        }

        int Clamp(int page) => Mathf.Clamp(page, 0, Mathf.Max(m_PageCount - 1, 0));

        void Rebuild()
        {
            m_Items.Clear();
            foreach (var page in VisiblePages(m_PageCount, m_Value))
            {
                if (page == Ellipsis)
                {
                    var ellipsis = new Label("…");
                    ellipsis.AddToClassList("hone-pagination__ellipsis");
                    ellipsis.AddToClassList("hone-text");
                    m_Items.Add(ellipsis);
                    continue;
                }

                var isCurrent = page == m_Value;
                var item = new Button
                {
                    text = (page + 1).ToString(),
                    variant = isCurrent ? Button.Variant.Outline : Button.Variant.Ghost,
                };
                item.AddToClassList("hone-pagination__item");
                item.EnableInClassList(CurrentClass, isCurrent);
                item.clicked += () => value = page;
                m_Items.Add(item);
            }

            m_Previous.SetEnabled(m_Value > 0);
            m_Next.SetEnabled(m_Value < m_PageCount - 1);
        }

        // 表示するページ番号（0 始まり）の並び。Ellipsis は省略記号。
        // 7 ページ以下なら全ページ、8 ページ以上なら常に 7 項目（省略記号を含む。今のページが端から離れているときは、先頭、省略記号、今のページとその両隣、省略記号、最後）。
        // 7、3、4、5 の値は「7 項目、両隣 1 つずつ」を前提にした組なので、両隣の数を変えるときは全部一緒に変える
        static IEnumerable<int> VisiblePages(int count, int current)
        {
            if (count <= 7)
            {
                for (var i = 0; i < count; i++)
                    yield return i;
                yield break;
            }

            if (current <= 3)
            {
                for (var i = 0; i <= 4; i++)
                    yield return i;
                yield return Ellipsis;
                yield return count - 1;
                yield break;
            }

            if (current >= count - 4)
            {
                yield return 0;
                yield return Ellipsis;
                for (var i = count - 5; i < count; i++)
                    yield return i;
                yield break;
            }

            yield return 0;
            yield return Ellipsis;
            yield return current - 1;
            yield return current;
            yield return current + 1;
            yield return Ellipsis;
            yield return count - 1;
        }

        const string CurrentClass = "is-current";
    }
}
