using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.ModelRooms
{
    // レトロインディー RPG 風のモデルルーム。段階 2 の見た目は RetroIndieRpg.uss、段階 3 の演出は Inject が足す。
    // ループする演出（ハートの揺れ）は、schedule で舞台に状態クラスを付け外しし、動きは USS に任せる。
    // 部品は演出を持たないので、使う口は部品の設定（charactersPerSecond、commitDelay）と状態（.is-holding、:focus）だけ。
    // ▼ が見えるかどうかは部品の .is-waiting（MessageWindow.uss の visibility）に任せる。
    public static class RetroIndieRpgRoom
    {
        public const string ClassName = "room-retro-indie-rpg";

        const float CharactersPerSecond = 40f;
        const float CommitDelaySeconds = 0.05f;
        const long SwayIntervalMs = 250;

        // テストが登録を外して戻すときにも使う
        public static readonly ModelRoom Room = new ModelRoom
        {
            displayName = "レトロインディー RPG 風",
            className = ClassName,
            // 語の区切りは全角の空白。話し手の印「＊」は名札ではなく本文に混ぜ、折り返した行は 2 字下げる（書き出しの位置はページによらず同じ）。
            // 文面は仮の表記（公式の日本語を確かめていない）。環境の描写の 1 行目は、このルームで書き足した
            pages = new[]
            {
                new MessageWindow.Page(null, "＊　ちいさな　ひかりが\n　　ゆれている。"),
                new MessageWindow.Page(null, "＊　ケツイが　みなぎった。"),
            },
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

            var sway = stage.schedule.Execute(() => stage.ToggleInClassList("is-sway")).Every(SwayIntervalMs);
            // ルームを選び直すと列ごと組み直されるので、外れた舞台の演出は止める
            stage.RegisterCallbackOnce<DetachFromPanelEvent>(_ => sway.Pause());
        }
    }
}
