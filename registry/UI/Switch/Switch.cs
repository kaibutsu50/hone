using UnityEngine.UIElements;

namespace Hone
{
    // 基底は完全修飾で書く。Hone.Toggle ができると、namespace Hone の中の修飾しない Toggle は Hone.Toggle を指す。
    // value、ChangeEvent<bool>、クリックと NavigationSubmitEvent での切り替え、SetValueWithoutNotify は基底のものをそのまま使う。
    // Switch 自身は振る舞いを足さず、基底が付けるクラスの付け替えだけを行う。
    // 文言は label で渡す（text の Label は基底が track の中に作る。README）。
    [UxmlElement]
    public partial class Switch : UnityEngine.UIElements.Toggle
    {
        public Switch()
        {
            // 既定テーマの見た目（.unity-toggle）を外す。unity-base-field 系のクラスは残す。
            // hone-focusable は付けない。root に付けると label を含む行全体が ring で囲まれるので、ring は Switch.uss が track の border で描く
            RemoveFromClassList(UnityEngine.UIElements.Toggle.ussClassName);
            AddToClassList("hone-switch");
            AddToClassList("hone-text");

            // 基底の visualInput（root の直接の子）が track、その checkmark（track の直接の子）が thumb
            var track = this.Q(className: UnityEngine.UIElements.Toggle.inputUssClassName);
            track.RemoveFromClassList(UnityEngine.UIElements.Toggle.inputUssClassName);
            track.AddToClassList("hone-switch__track");

            var thumb = this.Q(className: UnityEngine.UIElements.Toggle.checkmarkUssClassName);
            thumb.RemoveFromClassList(UnityEngine.UIElements.Toggle.checkmarkUssClassName);
            thumb.AddToClassList("hone-switch__thumb");
        }
    }
}
