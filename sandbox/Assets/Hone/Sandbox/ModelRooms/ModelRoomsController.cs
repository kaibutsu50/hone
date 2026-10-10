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
    // Register は sandbox 側のファイルから、[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] で呼ぶ。
    // registry/ 配下のコードには書かない（配布物が sandbox の asmdef に依存してしまう）。
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

        // 3 列を入れる要素（name `columns`）。UI が読み込まれ、最初の 3 列を組み終えるまでは null。テストと eval が列を探す入口
        public VisualElement columns { get; private set; }

        // 同じ className の再登録は置き換える（Domain Reload 無効でも二重に並ばない）。
        // 異なる className の登録は残り続けるので、テストなどで登録したものは Unregister で消す
        public static void Register(ModelRoom room)
        {
            if (room == null)
                throw new ArgumentNullException(nameof(room));
            if (string.IsNullOrEmpty(room.className))
                throw new ArgumentException("className is empty", nameof(room));
            if (string.IsNullOrEmpty(room.displayName))
                throw new ArgumentException("displayName is empty", nameof(room));

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
            // 失敗したときに前の UI を操作しないよう、先に捨てる（SelectRoom は InvalidOperationException で返る）
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

            var selectable = s_Rooms.ToList();
            // 例外を出しうる最初の組み立てを、dropdown を配線する前に済ませる。
            // 失敗したときに、配線済みで列が空の半端な状態を残さない（m_Room と columns が null のままなので、SelectRoom が例外を返す）
            BuildColumns(container, selectable.Count == 0 ? null : selectable[0]);

            room.choices = selectable.Count == 0
                ? new List<string> { NoRoomName }
                : selectable.Select(r => r.displayName).ToList();
            room.SetValueWithoutNotify(room.choices[0]);
            room.RegisterValueChangedCallback(_ => BuildColumns(container, selectable.Count == 0 ? null : selectable[room.index]));
            m_Selectable.AddRange(selectable);
            m_Room = room;
            columns = container;
        }

        // room（null なら素）で 3 列を組み直す。2 列目と 3 列目の舞台にルームの className を付け、3 列目の舞台には attach の時点で inject を 1 回呼ぶ。
        // container は panel に付いているので、attach は Add の中で起きる。メッセージの Show は 3 列とも Add した後
        // （inject が MessageWindow の設定を変えても、1 ページ目から効く）
        void BuildColumns(VisualElement container, ModelRoom room)
        {
            container.Clear();
            container.Add(BuildColumn(StageNames[0], null, false));
            container.Add(BuildColumn(StageNames[1], room, false));
            container.Add(BuildColumn(StageNames[2], room, true));

            foreach (var message in container.Query<MessageWindow>().ToList())
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

            // inject の例外はログに出して続ける。止めると、残りの列のメッセージが出ないまま組み立てが終わる
            if (inject && room?.inject != null)
                stage.RegisterCallbackOnce<AttachToPanelEvent>(evt =>
                {
                    try
                    {
                        room.inject(stage);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e, this);
                    }
                });

            var message = Require<MessageWindow>(stage, "message");
            var dialog = Require<Dialog>(stage, "yes-no");
            message.completed += dialog.Open;
            Require<Hone.Button>(stage, "yes").committed += () => Commit(dialog, status, "はい");
            Require<Hone.Button>(stage, "no").committed += () => Commit(dialog, status, "いいえ");
            again.clicked += () =>
            {
                dialog.Close();
                message.Show(Pages);
            };

            return column;
        }

        static T Require<T>(VisualElement stage, string name) where T : VisualElement =>
            stage.Q<T>(name) ?? throw new InvalidOperationException(
                $"ModelRooms: {typeof(T).Name} named '{name}' was not found (Message or YesNo is not Message.uxml / YesNo.uxml, or it has no such element)");

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
