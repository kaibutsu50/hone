using System;
using UnityEngine.UIElements;

namespace Hone.Sandbox.ModelRooms
{
    // モデルルームの定義。構造（UXML）は全ルーム共通で、ルームが変えるのは USS と演出のスクリプトだけ。
    // ModelRoomsController.Register で登録する。登録は sandbox 側のファイルから呼ぶ（registry/ 配下のコードには書かない）。
    public sealed class ModelRoom
    {
        // dropdown に出す名前（ジャンル名。例: 「クラシック JRPG 風」）
        public string displayName;

        // 2 列目と 3 列目の舞台に付ける USS クラス（例: "room-classic-jrpg"）。登録の識別子でもある
        public string className;

        // 3 列目の舞台を受け取り、演出を足す。舞台が panel に attach された後に、組むたびに 1 回呼ぶ。null なら何もしない
        public Action<VisualElement> inject;
    }
}
