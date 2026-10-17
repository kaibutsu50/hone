using UnityEngine.UIElements;

namespace Hone
{
    // 基底は完全修飾で書く（Hone の他の部品と揃える）。
    // value、lowValue、highValue、title、ChangeEvent<float> は基底のものをそのまま使う。
    // Progress 自身は振る舞いを足さず、基底が付けるクラスの付け替えだけを行う。
    // 進捗の幅は基底が決める（value を設定した時点と track の大きさが変わった時点で、indicator の inline の right を px で書き換える）
    [UxmlElement]
    public partial class Progress : UnityEngine.UIElements.ProgressBar
    {
        public Progress()
        {
            // 既定テーマの見た目（unity-progress-bar 系）を外して、hone-progress 系に付け替える。
            // indicator の position: absolute も既定テーマが持っていたので、Progress.uss が持ち直す
            Replace(this, ussClassName, "hone-progress");
            Replace(this.Q(className: containerUssClassName), containerUssClassName, "hone-progress__container");
            Replace(this.Q(className: backgroundUssClassName), backgroundUssClassName, "hone-progress__track");
            Replace(this.Q(className: progressUssClassName), progressUssClassName, "hone-progress__indicator");
            Replace(this.Q(className: titleContainerUssClassName), titleContainerUssClassName, "hone-progress__title-container");

            var title = this.Q(className: titleUssClassName);
            Replace(title, titleUssClassName, "hone-progress__title");
            title.AddToClassList("hone-text");
        }

        static void Replace(VisualElement element, string from, string to)
        {
            element.RemoveFromClassList(from);
            element.AddToClassList(to);
        }
    }
}
