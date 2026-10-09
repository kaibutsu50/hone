# MessageWindow

複数ページの文章を 1 ページずつ出し、Submit（またはクリック）で次へ送るメッセージウィンドウ。文字送り（1 文字ずつ表示）を持つ。
既定は即時表示で、速さは `charactersPerSecond` で受け取る。headless 層（`Hone.Core`）の `FocusScope` や `BackStack` は使わない（下の「headless 層との関係」）。
話者名、選択肢、送りの音、1 文字ごとのイベントは持たない。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:hone="Hone">
    <hone:MessageWindow name="message-window" text="Save your changes and close this screen?" />
</ui:UXML>
```

```csharp
var window = root.Q<Hone.MessageWindow>("message-window");
window.charactersPerSecond = 30;
window.completed += OnMessageCompleted;
window.Show("最初のページ", "次のページ", "最後のページ");
// Submit で送るには、ウィンドウにフォーカスが要る（クリックはフォーカスが無くても届く）
window.Focus();
```

slot は持たず、UXML の子要素は想定しない（入れると ▼ の後ろに並ぶ）。中身は text の `Label` と ▼ の `Label` で、コンストラクタが作る。
`text` 属性は 1 ページだけを出す UXML での確認用。複数ページは `Show()` で渡す。

## API

| 名前 | 意味 |
|---|---|
| `text` | 属性。set は `Show(value)` と同じだが、空文字列と `null` はページ無しになる（`Show("")` は空のページが 1 つになり、`Advance()` で `completed` が出る）。get は今のページの文字列（ページが無ければ空文字列） |
| `charactersPerSecond` | 属性。文字送りの速さ。既定 0。ページの開始時に 0 以下（NaN と無限大も）なら即時表示。文字送りの途中で変えると、その時点から新しい速さで進む（出ている文字は減らない）。0 以下にすると全文が出る |
| `Show(params string[] pages)` | 1 ページ目から出し直す。送りの途中でも最初からやり直す。`null` と空の配列はページ無し。`null` のページは空文字列 |
| `Advance()` | Submit とクリックと同じ処理（下の表） |
| `pageIndex` | 今のページ。ページが無いときは -1（読み取りのみ） |
| `pageCount` | ページ数（読み取りのみ） |
| `completed` | 最後のページで全文が出た後に `Advance()` されたとき 1 回出るイベント。次の `Show` まで再び出ない |

## Advance の処理

入力は `NavigationSubmitEvent`（フォーカスがあるとき）と `ClickEvent`（ウィンドウのどこでも）の 2 つだけ。どちらも `Advance()` を呼び、祖先には伝播させない。
押すたびに 1 歩進む。出ている途中なら全部出し、出きっていれば次へ。

| 今の状態 | Advance したとき |
|---|---|
| ページが無い（`Show` 前） | 何もしない |
| 文字送りの途中 | そのページの全文を出す（ページは進めない） |
| 全文が出ていて、次のページがある | 次のページへ進み、文字送りを始める |
| 最後のページで全文が出ている（`completed` 前） | `completed` を 1 回出す |
| `completed` の後 | 何もしない（次の `Show` まで） |

## USS クラス

| クラス | 付く要素 |
|---|---|
| `hone-message-window` | MessageWindow 自身。`hone-focusable` も付く（Submit を受け取るためにフォーカスを取る。`tabIndex = 0`） |
| `hone-message-window__text` | ページの文字列の `Label`。折り返す |
| `hone-message-window__indicator` | ▼ の `Label`。`is-waiting` の間だけ見える |
| `is-revealing` | MessageWindow に付く状態クラス。文字送りの途中の間 |
| `is-waiting` | MessageWindow に付く状態クラス。全文が出ていて、次のページがある間（▼ が見える） |

最後のページで全文が出ているときは、`is-revealing` も `is-waiting` も付かない。
部品は演出を持たない。▼ の点滅や揺れ、送りの音は、この 2 つの状態クラスを使って利用者が書く。

幅は親に従い、高さは内容に従う。ページで行数が変わると高さが変わる。
フォーカスの ring（`.hone-focusable`）は border の幅を `--hone-border-width` から `--hone-ring-width` に変えるので、その分だけ text の幅が狭まり、折り返しと高さが変わることがある。クリックもフォーカスを取るので、最初のクリックで変わりうる。

## 文字送り

- text の `Label` に rich text を組んで出す。表示済みの部分はそのまま、未表示の部分を `<alpha=#00>` で透明にする。未表示の文字も描かれる（透明なだけ）ので、行の折り返しと高さは、送りの途中でも全文のときと同じになる。
- ページの文字列は rich text として解釈しない。`<` やタグの形（`HP<10 <b>bold</b>`）、`</noparse>` そのものを含んでいても、送りの途中でも文字のまま出る。
- 1 文字は `System.Globalization.StringInfo` の text element 単位で数える。結合文字やサロゲートペアは 1 文字。
  ZWJ でつないだ絵文字と国旗は複数に数えるので途中の状態が見え、タイ語の SARA AM（ำ）は 2 回に分けて出る。
  この数え方は Unity 6000.7.0b2 の Mono 6.13 で確かめたもので、ランタイムが変わると変わりうる。
- 時間は `schedule.Execute(...).Every(16)` で進め、tick ごとの経過時間 × `charactersPerSecond` を足し上げ、切り捨てた数だけ出す。`Update` は使わない。
- panel から外れたら送りを止め、そのページの全文を出した状態にする（付け直しても再開しない）。
- 方式の確認は、素の `Label`（幅 180px、16px）で行った（sandbox/README.md の「RoomExp の検証」の (e)、6000.7.0b2）。
  Advanced Text Generator では、ja、en、ar、th の途中までの表示で、行の折り返しと高さが全文のときと同じだった。
  Standard の生成器（`-unity-text-generator: standard`）では ja と en だけを確かめた。`</noparse>` のエスケープと、ar と th の字形は確かめていない。

## headless 層との関係

C# では `Hone.Core`（`FocusScope`、`BackStack`）を使わない。見た目は Core.uss の `.hone-focusable`（focus ring）と `.hone-text`（本文フォント）に依存するので、registry の依存に `Core` がある。
