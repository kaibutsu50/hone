using System;
using UnityEngine.UIElements;

namespace Hone
{
    // 基底は完全修飾で書く。namespace Hone の中では、修飾しない Button が Hone.Button（自分自身）を指す。
    // clicked、text、NavigationSubmitEvent の処理は基底のものをそのまま使う。
    // 確定までの間（Hold）は committed で受ける。clicked は押した瞬間に出るままで、遅らせない。
    [UxmlElement]
    public partial class Button : UnityEngine.UIElements.Button
    {
        public enum Variant { Default, Outline, Ghost, Destructive }

        Variant m_Variant;
        IVisualElementScheduledItem m_Commit;

        // 変えると hone-button--{variant} のクラスを付け替える。Default はクラスなし
        [UxmlAttribute]
        public Variant variant
        {
            get => m_Variant;
            set
            {
                if (m_Variant != Variant.Default)
                    RemoveFromClassList(VariantClass(m_Variant));
                m_Variant = value;
                if (m_Variant != Variant.Default)
                    AddToClassList(VariantClass(m_Variant));
            }
        }

        // 押してから committed までの秒数。0 以下なら押した瞬間に committed を出す。Hold の途中で変えても、進行中の Hold には効かない
        [UxmlAttribute]
        public float commitDelay { get; set; }

        // 確定。commitDelay が 0 以下なら押した処理の中で、そうでなければ commitDelay 秒後に 1 回出る
        public event Action committed;

        // Hold の間 true。見た目は .is-holding で付ける
        public bool isHolding { get; private set; }

        public Button()
        {
            // 既定テーマの見た目（.unity-button）を外す
            RemoveFromClassList(UnityEngine.UIElements.Button.ussClassName);
            AddToClassList("hone-button");
            AddToClassList("hone-focusable");
            AddToClassList("hone-text");

            clicked += OnClicked;
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        void OnClicked()
        {
            if (isHolding)
                return;

            if (commitDelay <= 0f)
            {
                committed?.Invoke();
                return;
            }

            isHolding = true;
            AddToClassList(HoldingClass);
            m_Commit = schedule.Execute(OnCommit).StartingIn((long)(commitDelay * 1000f));
        }

        void OnCommit()
        {
            EndHold();
            committed?.Invoke();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            if (!isHolding)
                return;

            m_Commit?.Pause();
            EndHold();
        }

        void EndHold()
        {
            m_Commit = null;
            isHolding = false;
            RemoveFromClassList(HoldingClass);
        }

        const string HoldingClass = "is-holding";

        static string VariantClass(Variant value) => "hone-button--" + value.ToString().ToLowerInvariant();
    }
}
