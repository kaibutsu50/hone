# Cycle

「‹ ふつう ›」のように、左右で選択肢を 1 つずつ巡る部品。ゲームの設定画面（難易度、画質、言語など）で、ドロップダウンの代わりに使う。shadcn に対応物は無い。
持つのは選択肢の文字列の列（`choices`）と選択中の index（`value`、0 始まり）だけ。選択肢ごとの値（enum や設定値）との対応は、利用者が `value` から引く。
部品全体が 1 つのフォーカス停止点で、フォーカスを当てて左右 1 手で値が変わる。前後の Button はマウスとタッチ用で、フォーカスを持たない。variant は持たない（前後の Button は Ghost に固定）。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:hone="Hone">
    <hone:Cycle choices="Low,Medium,High" value="1" />
</ui:UXML>
```

`choices` は `DropdownField` の `choices` と同じく、カンマ区切りで書く（`choices="Low,Medium,High"` で 3 つになった。Unity 6000.7.0b4 で確認）。
UXML の属性は、UXML に書いた順ではなく C# の宣言の順に適用される。`choices` と `wrap` を `value` より先に宣言してあるので、UXML ではどちらを先に書いても `value="1"` は残る。

C# で作り、値の変化を受けて設定値を引く例:

```csharp
enum Quality { Low, Medium, High }

var cycle = new Hone.Cycle
{
    choices = new List<string> { "Low", "Medium", "High" },
    value = (int)Quality.Medium,
};

// 選択肢が変わったら、index から設定値を引く
cycle.RegisterValueChangedCallback(evt => ApplyQuality((Quality)evt.newValue));

// ChangeEvent を出さずに値を変える（保存した設定を読み込んだときなど）
cycle.SetValueWithoutNotify((int)savedQuality);
```

C# で `choices` と `value` を一緒に設定するときは、`choices` を先に書く。`value` は `choices` の範囲に収められるので、逆だと空の `choices` に収められて 0 になる。

## API

| 名前 | 意味 |
|---|---|
| `choices` | 選択肢。既定は空。set は受け取った列を写して持つ（後から元の列を書き換えても反映しない。変えるときは列を入れ直す）。null は空として扱う。`value` を範囲に収めるが、`ChangeEvent` は出さない |
| `wrap` | 端で巡回するか。既定 false。false なら端でその側の Button が disabled になり、左右の入力も端で止まる。true なら前後の Button は常に有効（`choices` が 2 つ以上のとき）で、端から反対側へ巡る |
| `value` | 選択中の index（0 始まり）。既定 0。`0`〜`max(choices.Count - 1, 0)` に収め、変わったときだけ `ChangeEvent<int>` を 1 回出す。`wrap` は set には効かない（-1 を入れても最後にはならず 0 に収まる） |
| `SetValueWithoutNotify(int)` | `value` と同じに収めて表示を更新するが、`ChangeEvent` を出さない |

| 操作 | 結果 |
|---|---|
| 前の Button、またはフォーカス中の左 | 1 つ前へ。最初のとき、`wrap` なら最後へ、そうでなければ何もしない |
| 次の Button、またはフォーカス中の右 | 1 つ後へ。最後のとき、`wrap` なら最初へ、そうでなければ何もしない |
| フォーカス中の上下、Tab（Next / Previous） | 消費しない。既定のナビゲーション（または `FocusScope` の trap）で、フォーカスが部品の外へ移る |
| Submit、Cancel | 何もしない（Cancel は `BackStack` に届く） |

`choices` が 0 か 1 のときは、`wrap` に関わらず前後の Button が両方 disabled になる。0 のときの表示は空文字列。
操作させたくないときは、Cycle に `SetEnabled(false)` する。前後の Button も disabled の見た目になり、root はフォーカスを受けなくなる。

## USS クラス

| クラス | 意味 |
|---|---|
| `hone-cycle` | ブロック。横並び。フォーカス ring を描くための透明な border を持つ |
| `hone-focusable` | root に付く。フォーカス ring は root に出る（`Core.uss`）。前後の Button には出ない |
| `hone-cycle__previous` | 前の Button（`Hone.Button`、Ghost、"‹"、focusable でない） |
| `hone-cycle__value` | 選択中の文字列（`Label`）。`hone-text` も付く。幅は親に従って伸び、狭ければ省略記号で切れる |
| `hone-cycle__next` | 次の Button（`Hone.Button`、Ghost、"›"、focusable でない） |

前後の Button の見た目は `Button.uss` のまま。Cycle の USS では Button を上書きしない。

## 前後の記号の差し替え

前後の Button はコンストラクタで 1 回だけ作るので、取り出して `text` を変えれば残る。

```csharp
cycle.Q<Hone.Button>(className: "hone-cycle__previous").text = "◀";
cycle.Q<Hone.Button>(className: "hone-cycle__next").text = "▶";
```

## headless 層との関係

- root が `NavigationMoveEvent` を TrickleDown で受け、自分が target のときだけ左右を処理する。左右は `FocusController.IgnoreEvent` で既定の移動を止めてから値を動かす。端で値が動かないときも止める（止めないと、端でだけ隣の要素へフォーカスが移る）。
- Cycle は `Hone.Core.INavigationMoveConsumer` を実装し、左右で true を返す。`FocusScope { trap = true }` の中（Dialog の中など）では、FocusScope が先に `NavigationMoveEvent` を受けるが、左右は Cycle に任せる。上下と Tab は今までどおり FocusScope の trap が処理する。
- `BackStack` は使わない。Cancel は基底のまま上へ届く。
- 前後の Button を押しても、root にフォーカスは移らない。

## World Space での置き方

World Space の panel でも、Screen Space と同じ構造のまま置ける。Cycle 側に World Space 用の設定は無い。
幅は親で決める。中央の Label が伸び縮みし、前後の Button は縮まない。panel が小さいときは、前後の Button と、最も長い選択肢が切れずに入る幅を親に与える。
