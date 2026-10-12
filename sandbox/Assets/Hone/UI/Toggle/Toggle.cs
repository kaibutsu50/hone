using UnityEngine.UIElements;

namespace Hone
{
    // 基底は完全修飾で書く。namespace Hone の中では、修飾しない Button は Hone.Button、Toggle は自分自身（Hone.Toggle）を指す。
    // clicked、text、NavigationSubmitEvent の処理は基底の Button のものをそのまま使い、Toggle は on / off の値を足す。
    // on の状態は :checked で表せない（VisualElement.pseudoStates は internal）ので、.is-pressed クラスで表す。
    [UxmlElement]
    public partial class Toggle : UnityEngine.UIElements.Button, INotifyValueChanged<bool>
    {
        bool m_Value;

        // 変えたときだけ ChangeEvent<bool> を 1 回出し、.is-pressed を付け外しする
        [UxmlAttribute]
        public bool value
        {
            get => m_Value;
            set
            {
                if (m_Value == value)
                    return;

                var previous = m_Value;
                SetValueWithoutNotify(value);
                using (var evt = ChangeEvent<bool>.GetPooled(previous, value))
                {
                    evt.target = this;
                    SendEvent(evt);
                }
            }
        }

        public Toggle()
        {
            // 既定テーマの見た目（.unity-button）を外す
            RemoveFromClassList(UnityEngine.UIElements.Button.ussClassName);
            AddToClassList("hone-toggle");
            AddToClassList("hone-focusable");
            AddToClassList("hone-text");

            clicked += OnClicked;
        }

        void OnClicked() => value = !value;

        // ChangeEvent を出さずに値と .is-pressed だけを変える
        public void SetValueWithoutNotify(bool newValue)
        {
            m_Value = newValue;
            EnableInClassList(PressedClass, newValue);
        }

        const string PressedClass = "is-pressed";
    }
}
