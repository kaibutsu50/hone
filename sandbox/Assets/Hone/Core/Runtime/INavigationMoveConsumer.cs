using UnityEngine.UIElements;

namespace Hone.Core
{
    // フォーカスを持つ要素が、方向入力を自分で消費することを FocusScope に伝える。
    // FocusScope は、target がこれを実装し true を返す方向では IgnoreEvent も Focus() もしない。
    // true を返す方向では、実装側が自分の NavigationMoveEvent の handler で IgnoreEvent を呼んで既定の移動を止める（呼ばないと、既定のナビが trap の外へフォーカスを出す）。
    // FocusScope が見るのは NavigationMoveEvent の target 自身だけで、その祖先が実装していても見ない
    public interface INavigationMoveConsumer
    {
        bool ConsumesNavigationMove(NavigationMoveEvent.Direction direction);
    }
}
