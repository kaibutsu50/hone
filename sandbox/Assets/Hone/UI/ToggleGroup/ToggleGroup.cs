using UnityEngine.UIElements;

namespace Hone
{
    // 基底は完全修飾で書く。namespace Hone の中では、修飾しない Button は Hone.Button を指す。
    // 選択の状態（value）、ChangeEvent<ToggleButtonGroupState>、SetValueWithoutNotify、isMultipleSelection、allowEmptySelection は基底のものをそのまま使う。
    // 選ばれている子の Button に付く :checked も基底が付ける。ToggleGroup はクラスの付け替えだけを持つ。
    [UxmlElement]
    public partial class ToggleGroup : UnityEngine.UIElements.ToggleButtonGroup
    {
        public ToggleGroup()
        {
            // 既定テーマの見た目（.unity-toggle-button-group）を外す。コンテナと子の Button に基底が付けるクラスは外さない
            RemoveFromClassList(UnityEngine.UIElements.ToggleButtonGroup.ussClassName);
            AddToClassList("hone-toggle-group");
        }
    }
}
