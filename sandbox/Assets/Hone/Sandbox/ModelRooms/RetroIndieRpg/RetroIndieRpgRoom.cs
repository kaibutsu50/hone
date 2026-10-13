using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.ModelRooms
{
    // レトロインディー RPG 風のモデルルーム。段階 2 の見た目は RetroIndieRpg.uss、段階 3 の演出は Inject が足す。
    // メッセージは会話の窓、はい / いいえ の Dialog はセーブの窓として見せる（文面は question / yes / no）。
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
            // 語の区切りは全角の空白。話し手の印「＊」は名札ではなく本文に混ぜ、改行した 2 行目は 2 字下げる（書き出しの位置はページによらず同じ）。
            // 1 行は 18 字まで（窓の内側の幅 262px を 14px の字で割った数）。それより長い行は折り返して字下げが崩れるので、改行を入れる
            pages = new[]
            {
                new MessageWindow.Page(null, "＊（いつかネズミは　すあなからでて\n　　チーズを　かじるかもしれない…）"),
                new MessageWindow.Page(null, "＊（そうおもったら\n　　ケツイが　みなぎった）"),
                // 2・3 列目では、このページだけ本文を赤で出す（1 列目は素のまま。page-determination は RetroIndieRpg.uss）
                new MessageWindow.Page(null, "ケツイ", "page-determination"),
            },
            // はい / いいえ の Dialog はセーブの窓として使う。問いは 1 行目に記録の名前・LV・時間、1 行空けて中央に「―」。
            // 桁は全角（14px）と半角（7px）の空白でそろえる（RetroIndieRpg.uss の pre-wrap）。参考の文言から少し変えてある
            question = "きろくなし　　　 LV 1　　 0:00\n\n　　　　　　　 ―",
            yes = "きろく",
            no = "やめる",
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
