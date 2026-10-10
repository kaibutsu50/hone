using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.ModelRooms
{
    // テキストアドベンチャー風のモデルルーム。段階 2 の見た目は TextAdventure.uss、段階 3 の演出は Inject が足す。
    // 話者は名札（MessageWindow.Page の speaker）で出す。選択肢の場面の問いと文面は ModelRoom の question / yes / no で渡す。
    // ループする演出（▼ の点滅）は、schedule で舞台に状態クラスを付け外しし、見た目は USS に任せる。
    // 部品は演出を持たないので、使う口は部品の設定（charactersPerSecond、commitDelay）と状態（.is-holding、.is-waiting）だけ。
    public static class TextAdventureRoom
    {
        public const string ClassName = "room-text-adventure";

        const float CharactersPerSecond = 30f;
        const float CommitDelaySeconds = 0.15f;
        const long BlinkIntervalMs = 400;

        // テストが登録を外して戻すときにも使う
        public static readonly ModelRoom Room = new ModelRoom
        {
            displayName = "テキストアドベンチャー風",
            className = ClassName,
            // 話者は名札に出す（本文には混ぜない）。3 ページ目と 4 ページ目は 2 行
            pages = new[]
            {
                new MessageWindow.Page("ジョシュ", "あ、ハシゴだ。"),
                new MessageWindow.Page("タンテイ", "それはキャタツだよ。"),
                new MessageWindow.Page("ジョシュ", "どう違うの？\n同じようなものじゃない。"),
                new MessageWindow.Page("ジョシュ", "もっとホンシツをみようよ。\nたんていさん。"),
            },
            // 選択肢の場面の問いは心の声（括弧書き）。2 行目の頭の全角の空白は、括弧の内側に字下げを揃えるため
            question = "（まだわかってないなら、\n　これが最後のチャンス、か）",
            yes = "もちろん、尋問する",
            no = "もちろん、尋問しない",
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
            // ルームを選び直すと列ごと組み直されるので、外れた舞台の演出は止める
            stage.RegisterCallbackOnce<DetachFromPanelEvent>(_ => blink.Pause());
        }
    }
}
