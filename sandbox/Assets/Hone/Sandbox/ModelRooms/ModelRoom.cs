using System;
using UnityEngine.UIElements;

namespace Hone.Sandbox.ModelRooms
{
    // モデルルームの定義。構造（UXML）は全ルーム共通で、ルームが変えるのは USS と演出のスクリプトだけ。
    // ModelRoomsController.Register で登録する。
    public sealed class ModelRoom
    {
        // dropdown に出す名前（ジャンル名。例: 「クラシック JRPG 風」）。空は登録できない
        public string displayName;

        // 2 列目と 3 列目の舞台に付ける USS クラス（例: "room-classic-jrpg"）。登録の識別子でもあるので、Register の後で変えない（Unregister が効かなくなる）
        public string className;

        // メッセージのページ（ルームを選んだとき、3 列とも同じ文面を出す）。null か空なら既定の文面
        public string[] pages;

        // 3 列目の舞台を受け取り、演出を足す。舞台が panel に attach された後に、組むたびに 1 回呼ぶ。null なら何もしない
        public Action<VisualElement> inject;
    }
}
