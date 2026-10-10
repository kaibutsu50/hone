using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.ModelRooms
{
    // 同じ構造（Message.uxml の MessageWindow と YesNo.uxml の Dialog）を 3 列に並べる:
    // 1 列目は素（registry の既定のまま）、2 列目はルームの USS クラスだけ、3 列目はそれに加えて inject の演出。
    // 上の DropdownField（name `room`）で選んだルームを 2 列目と 3 列目に当て、選び直すたびに 3 列すべてを組み直す。
    // 列の組み立てと流れ（メッセージを送り切ると Dialog が開き、はい / いいえ で結果を出して閉じる）はこのクラスが持つ。部品の振る舞いは Hone の部品が持つ。
    // Register は sandbox 側のファイルから呼ぶ（registry/ 配下のコードには書かない）。
    // 呼ぶ時点は [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]。
    // dropdown は PanelRenderer の UI が読み込まれた時点の登録内容で作る（それより後の Register / Unregister は dropdown に反映されない）。
    // 型名は、Hone の部品（Hone.Button、Hone.Dialog、Hone.MessageWindow）と Unity 標準の Button を取り違えないよう、Button だけ完全修飾で書く。
    [RequireComponent(typeof(PanelRenderer))]
    public class ModelRoomsController : MonoBehaviour
    {
        const string NoRoomName = "（ルームなし）";

        static readonly string[] StageNames = { "素", "USS だけ", "注入あり" };

        static readonly string[] Pages =
        {
            "ここは はじまりの村です。",
            "北の森には 近づかないほうが いいでしょう。夜になると 道が わからなくなります。",
            "旅の記録を 残していきますか？",
        };

        static readonly List<ModelRoom> s_Rooms = new List<ModelRoom>();

        [SerializeField] VisualTreeAsset m_Message;
        [SerializeField] VisualTreeAsset m_YesNo;

        // dropdown の選択肢（ルームなしの項目は含まない）。index が dropdown の index と対応する
        readonly List<ModelRoom> m_Selectable = new List<ModelRoom>();
        DropdownField m_Room;

        // 3 列を入れる要素（name `columns`）。UI が読み込まれるまでは null。eval とテストが列を探す入口
        public VisualElement columns { get; private set; }

        // 同じ className の再登録は置き換える（Domain Reload 無効でも二重に並ばない）。
        // 異なる className の登録は残り続けるので、テストなどで登録したものは Unregister で消す
        public static void Register(ModelRoom room)
        {
            if (room == null)
                throw new ArgumentNullException(nameof(room));
            if (string.IsNullOrEmpty(room.className))
                throw new ArgumentException("className is empty", nameof(room));

            var index = s_Rooms.FindIndex(r => r.className == room.className);
            if (index >= 0)
                s_Rooms[index] = room;
            else
                s_Rooms.Add(room);
        }

        public static void Unregister(string className)
        {
            s_Rooms.RemoveAll(r => r.className == className);
        }

        void Awake()
        {
            GetComponent<PanelRenderer>().RegisterUIReloadCallback((panelRenderer, root, version) => Rebuild(root));
        }

        void Rebuild(VisualElement root)
        {
            // 前の UI を指したまま残すと、この Rebuild が途中で止まったときに SelectRoom が前の要素を操作してしまう。
            // null にしておけば、SelectRoom は InvalidOperationException で eval に返る
            m_Room = null;
            columns = null;
            m_Selectable.Clear();

            if (m_Message == null || m_YesNo == null)
            {
                Debug.LogError("ModelRooms: Message or YesNo is not assigned", this);
                return;
            }
            var room = root.Q<DropdownField>("room");
            if (room == null)
            {
                Debug.LogError("ModelRooms: DropdownField named 'room' was not found (the PanelRenderer's UXML is not ModelRooms.uxml, or it has no such element)", this);
                return;
            }
            var container = root.Q("columns");
            if (container == null)
            {
                Debug.LogError("ModelRooms: element named 'columns' was not found (the PanelRenderer's UXML is not ModelRooms.uxml, or it has no such element)", this);
                return;
            }

            m_Selectable.AddRange(s_Rooms);
            room.choices = m_Selectable.Count == 0
                ? new List<string> { NoRoomName }
                : m_Selectable.Select(r => r.displayName).ToList();
            room.SetValueWithoutNotify(room.choices[0]);
            room.RegisterValueChangedCallback(_ => BuildColumns());
            m_Room = room;
            columns = container;
            BuildColumns();
        }

        // 選んでいるルーム（無ければ null）で 3 列を組み直す。2 列目と 3 列目の舞台にルームの className を付け、3 列目の舞台には attach の時点で inject を 1 回呼ぶ。
        // メッセージの Show は、3 列とも panel に付いて inject が済んだ後に行う（inject が MessageWindow の設定を変えても、1 ページ目から効く）
        void BuildColumns()
        {
            var room = m_Selectable.Count == 0 ? null : m_Selectable[m_Room.index];

            columns.Clear();
            columns.Add(BuildColumn(StageNames[0], null, false));
            columns.Add(BuildColumn(StageNames[1], room, false));
            columns.Add(BuildColumn(StageNames[2], room, true));

            foreach (var message in columns.Query<MessageWindow>().ToList())
                message.Show(Pages);
        }

        VisualElement BuildColumn(string stageName, ModelRoom room, bool inject)
        {
            var column = new VisualElement();
            column.AddToClassList("model-room-column");

            // 見出しは舞台の外に置く。ルームのテーマは舞台の中にしか掛からない
            var header = new VisualElement();
            header.AddToClassList("model-room-column__header");
            var title = new Label(stageName);
            title.AddToClassList("model-room-column__title");
            header.Add(title);
            var status = new Label();
            status.AddToClassList("model-room-status");
            header.Add(status);
            var again = new UnityEngine.UIElements.Button { name = "again", text = "もう一度" };
            header.Add(again);
            column.Add(header);

            var stage = new VisualElement();
            stage.AddToClassList("model-room-stage");
            if (room != null)
                stage.AddToClassList(room.className);
            m_Message.CloneTree(stage);
            // Dialog は舞台の全面を覆う absolute なので、舞台の最後の子にする（後ろの兄弟は Dialog の上に描かれる）
            m_YesNo.CloneTree(stage);
            column.Add(stage);

            if (inject && room?.inject != null)
                stage.RegisterCallbackOnce<AttachToPanelEvent>(evt => room.inject(stage));

            var message = stage.Q<MessageWindow>("message");
            var dialog = stage.Q<Dialog>("yes-no");
            message.completed += dialog.Open;
            stage.Q<Hone.Button>("yes").committed += () => Commit(dialog, status, "はい");
            stage.Q<Hone.Button>("no").committed += () => Commit(dialog, status, "いいえ");
            again.clicked += () =>
            {
                dialog.Close();
                message.Show(Pages);
            };

            return column;
        }

        static void Commit(Dialog dialog, Label status, string result)
        {
            status.text = result;
            dialog.Close();
        }

        // dropdown でルームを選んだのと同じ経路（ChangeEvent）で切り替える。eval からルームを切り替えて撮るための入口。
        // すでに選ばれている className を渡すと、ChangeEvent が出ないので何も起きない
        public void SelectRoom(string className)
        {
            if (m_Room == null || m_Room.panel == null)
                throw new InvalidOperationException("ModelRooms: the UI is not loaded yet");

            var index = m_Selectable.FindIndex(r => r.className == className);
            if (index < 0)
                throw new ArgumentException($"'{className}' is not selectable", nameof(className));
            m_Room.index = index;
        }
    }
}
