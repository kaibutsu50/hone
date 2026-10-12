using UnityEngine.UIElements;

namespace Hone.Core
{
    // フォーカスを持つ要素が、方向入力を自分で消費することを FocusScope に伝える。
    // FocusScope は、target がこれを実装し true を返す方向では IgnoreEvent も Focus() もしない
    public interface INavigationMoveConsumer
    {
        bool ConsumesNavigationMove(NavigationMoveEvent.Direction direction);
    }
}
