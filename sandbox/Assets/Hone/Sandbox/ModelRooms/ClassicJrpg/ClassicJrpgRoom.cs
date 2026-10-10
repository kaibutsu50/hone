using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.ModelRooms
{
    // クラシック JRPG 風のモデルルーム。段階 2 の見た目は ClassicJrpg.uss、段階 3 の演出は Inject が足す。
    // ループする演出（▼ の点滅、▶ の揺れ）は、schedule で舞台に状態クラスを付け外しし、動きは USS の transition に任せる。
    // 部品は演出を持たないので、使う口は部品の設定（charactersPerSecond、commitDelay）と状態クラス（.is-waiting、.is-holding、:focus）だけ。
    public static class ClassicJrpgRoom
    {
        public const string ClassName = "room-classic-jrpg";

        const float CharactersPerSecond = 30f;
        const float CommitDelaySeconds = 0.1f;
        const long BlinkIntervalMs = 500;
        const long SwayIntervalMs = 300;

        // テストが登録を外して戻すときにも使う
        public static readonly ModelRoom Room = new ModelRoom
        {
            displayName = "クラシック JRPG 風",
            className = ClassName,
            inject = Inject,
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            ModelRoomsController.Register(Room);
        }

        static void Inject(VisualElement stage)
        {
            stage.Q<MessageWindow>().charactersPerSecond = CharactersPerSecond;
            stage.Query<Hone.Button>().ForEach(button => button.commitDelay = CommitDelaySeconds);

            var blink = stage.schedule.Execute(() => stage.ToggleInClassList("is-blink-off")).Every(BlinkIntervalMs);
            var sway = stage.schedule.Execute(() => stage.ToggleInClassList("is-sway")).Every(SwayIntervalMs);
            // ルームを選び直すと列ごと組み直されるので、外れた舞台の演出は止める
            stage.RegisterCallbackOnce<DetachFromPanelEvent>(_ =>
            {
                blink.Pause();
                sway.Pause();
            });
        }
    }
}
