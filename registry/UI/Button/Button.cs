using UnityEngine.UIElements;

namespace Hone
{
    // 基底は完全修飾で書く。namespace Hone の中では、修飾しない Button が Hone.Button（自分自身）を指す。
    // clicked、text、NavigationSubmitEvent の処理は基底のものをそのまま使う。
    [UxmlElement]
    public partial class Button : UnityEngine.UIElements.Button
    {
        public enum Variant { Default, Outline, Ghost, Destructive }

        Variant m_Variant;

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

        public Button()
        {
            // 既定テーマの見た目（.unity-button）を外す
            RemoveFromClassList(UnityEngine.UIElements.Button.ussClassName);
            AddToClassList("hone-button");
            AddToClassList("hone-focusable");
            AddToClassList("hone-text");
        }

        static string VariantClass(Variant value) => "hone-button--" + value.ToString().ToLowerInvariant();
    }
}
