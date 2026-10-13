using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hone
{
    // namespace Hone の中では、修飾しない Button は Hone.Button を指す。
    // 構造: Tabs（.hone-tabs）> タブの列（Hone.ToggleGroup、.hone-tabs__list）+ 中身の入れ物（.hone-tabs__panels）。
    // UXML の子要素と Add() は中身の入れ物（contentContainer）に入り、i 番目の子が i 番目のタブの中身になる（.hone-tabs__content が付く）。
    // 選択はタブの列の ToggleGroup が持つ（Pointer と Submit で変わる。左右でタブを移っただけでは変わらない）。Tabs は選択に合わせて中身の display を切り替える。
    // フォーカスは、タブから Down で表示中の中身の最初の focusable へ、中身の最上段から Up で選択中のタブへ移す。それ以外の方向は既定のナビのまま。
    // タブの列が中身の上にある前提。
    [UxmlElement]
    public partial class Tabs : VisualElement, INotifyValueChanged<int>
    {
        const string ContentClass = "hone-tabs__content";
        const string ActiveClass = "is-active";

        readonly ToggleGroup m_List;
        readonly VisualElement m_Panels;
        readonly List<string> m_Tabs = new List<string>();
        int m_Value;
        bool m_Rebuilding;

        public override VisualElement contentContainer => m_Panels;

        // tabs は value より先に宣言する。UXML の属性は、UXML に書いた順ではなく宣言の順に適用される（Cycle の choices と同じ）

        // タブのラベル。set のたびにタブの列の Button を作り直す。get は写しを返し、set は受け取った列を写して持つ（Cycle の choices と同じ）。
        // null は空として扱う。value を範囲に収めるが、ChangeEvent は出さない
        [UxmlAttribute]
        public List<string> tabs
        {
            get => new List<string>(m_Tabs);
            set
            {
                m_Tabs.Clear();
                if (value != null)
                    m_Tabs.AddRange(value);
                // 作り直しの間に基底が index 0 を選び直しても（allowEmptySelection が false のとき、最初の子を足した時点で選ぶ）、value を変えない
                m_Rebuilding = true;
                m_List.Clear();
                foreach (var label in m_Tabs)
                    m_List.Add(new Button { text = label });
                m_Rebuilding = false;
                SetValueWithoutNotify(m_Value);
            }
        }

        // 選択中のタブの index（0 始まり）。0〜max(tabs.Count - 1, 0) に収め、変わったときだけ ChangeEvent<int> を 1 回出す
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
                SendChange(previous, clamped);
            }
        }

        public Tabs()
        {
            AddToClassList("hone-tabs");

            m_List = new ToggleGroup();
            m_List.AddToClassList("hone-tabs__list");
            m_List.RegisterValueChangedCallback(OnListChanged);

            m_Panels = new VisualElement();
            m_Panels.AddToClassList("hone-tabs__panels");

            // contentContainer を差し替えているので、自分の構造は hierarchy に直接足す
            hierarchy.Add(m_List);
            hierarchy.Add(m_Panels);

            // UXML の子要素は属性の後に足されるので、中身の表示は attach の時点で揃える。attach 後に Add() した中身は、次に選択が変わるまで揃わない
            RegisterCallback<AttachToPanelEvent>(evt => RefreshContents());
            // TrickleDown で受けるのは、中身の中の要素が自分の handler で処理する前に、上下の移動を決めるため
            RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
        }

        // ChangeEvent を出さずに、範囲に収めた値にしてタブの選択と中身の表示を揃える
        public void SetValueWithoutNotify(int newValue)
        {
            m_Value = Clamp(newValue);
            if (m_Tabs.Count > 0)
            {
                // ToggleButtonGroupState の length は子の数ではなく常に 64。全部外してから 1 つを選ぶ
                var state = m_List.value;
                for (var i = 0; i < state.length; i++)
                    state[i] = false;
                state[m_Value] = true;
                m_List.SetValueWithoutNotify(state);
            }
            RefreshContents();
        }

        int Clamp(int index) => Mathf.Clamp(index, 0, Mathf.Max(m_Tabs.Count - 1, 0));

        // Pointer と Submit でタブの選択が変わったとき
        void OnListChanged(ChangeEvent<ToggleButtonGroupState> evt)
        {
            if (m_Rebuilding)
                return;
            var selected = -1;
            for (var i = 0; i < evt.newValue.length; i++)
            {
                if (evt.newValue[i])
                {
                    selected = i;
                    break;
                }
            }
            if (selected < 0 || selected == m_Value)
                return;

            var previous = m_Value;
            m_Value = Clamp(selected);
            RefreshContents();
            SendChange(previous, m_Value);
        }

        void SendChange(int previous, int next)
        {
            using (var evt = ChangeEvent<int>.GetPooled(previous, next))
            {
                evt.target = this;
                SendEvent(evt);
            }
        }

        // 表示中の中身だけを display: flex にし、ほかを display: none にする。USS が読まれていなくても隠れるよう、inline で指定する（Dialog と同じ）
        void RefreshContents()
        {
            var index = 0;
            foreach (var content in m_Panels.Children())
            {
                var active = index == m_Value;
                content.AddToClassList(ContentClass);
                content.EnableInClassList(ActiveClass, active);
                content.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
                index++;
            }
        }

        void OnNavigationMove(NavigationMoveEvent evt)
        {
            var target = evt.target as VisualElement;
            if (target == null)
                return;

            if (evt.direction == NavigationMoveEvent.Direction.Down && m_List.Contains(target))
            {
                var content = ActiveContent();
                var focusables = new List<VisualElement>();
                if (content != null)
                    CollectFocusables(content, focusables);
                // 中身に focusable が無いときは、既定のナビに任せる
                if (focusables.Count == 0)
                    return;
                panel.focusController.IgnoreEvent(evt);
                focusables[0].Focus();
                return;
            }

            if (evt.direction == NavigationMoveEvent.Direction.Up && m_Tabs.Count > 0)
            {
                var content = ActiveContent();
                if (content == null || !content.Contains(target))
                    return;
                // 上の段に focusable があれば、中身の中の移動なので既定のナビに任せる
                if (HasFocusableAbove(content, target))
                    return;
                panel.focusController.IgnoreEvent(evt);
                // フォーカスの変更は非同期で、Focus() の直後に focusedElement を読んでも旧値のまま。この後で新しいフォーカスを前提にしない
                m_List.Query<Button>().AtIndex(m_Value)?.Focus();
            }
        }

        VisualElement ActiveContent() => m_Value < m_Panels.childCount ? m_Panels[m_Value] : null;

        // 中身の focusable のうち、中心が target の上端より上にあるもの（上の段）があるか。
        // 中身の local 座標で比べる。World Space の panel では worldBound の y が上向きで（Unity 6000.7.0b4 で実測）、単位も px ではないため
        static bool HasFocusableAbove(VisualElement content, VisualElement target)
        {
            var focusables = new List<VisualElement>();
            CollectFocusables(content, focusables);
            var top = content.WorldToLocal(target.worldBound).yMin;
            foreach (var focusable in focusables)
            {
                if (focusable != target && !focusable.Contains(target) && content.WorldToLocal(focusable.worldBound).center.y < top)
                    return true;
            }
            return false;
        }

        // 子孫のうち Focus() できる要素を DFS 順に集める（FocusScope の候補と同じ条件。隠れた要素の子孫も対象外）
        static void CollectFocusables(VisualElement parent, List<VisualElement> result)
        {
            foreach (var child in parent.Children())
            {
                if (child.resolvedStyle.display == DisplayStyle.None)
                    continue;
                if (child.canGrabFocus && child.tabIndex >= 0 && child.enabledInHierarchy)
                    result.Add(child);
                CollectFocusables(child, result);
            }
        }
    }
}
