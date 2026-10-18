# MessageWindow

複数ページの文章を 1 ページずつ出し、Submit（またはクリック）で次へ送るメッセージウィンドウ。文字送り（1 文字ずつ表示）を持つ。
既定は即時表示で、速さは `charactersPerSecond` で受け取る。headless 層（`Hone.Core`）の `FocusScope` や `BackStack` は使わない（下の「headless 層との関係」）。
話者はページごとに `Page` で渡すと名札の `Label` に出る。ページの最後に 1 個か 2 個の選択肢を出せる（下の「選択肢」）。送りの音、1 文字ごとのイベントは持たない。

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
// 話者を付けるときは Page で渡す。speaker が null か空文字列のページは名札が出ない
window.Show(
    new Hone.MessageWindow.Page("ジョシュ", "あ、ハシゴだ。"),
    new Hone.MessageWindow.Page("タンテイ", "それはキャタツだよ。"),
    new Hone.MessageWindow.Page(null, "（地の文）"));
// ページを消す（ページ無しにする）ときは型を付ける。Show(null) と Show() は、2 つの Show のどちらか決まらずコンパイルエラーになる
window.Show((string[])null);
// Submit で送るには、ウィンドウにフォーカスが要る（クリックはフォーカスが無くても届く）
window.Focus();
```

slot は持たず、UXML の子要素は想定しない（入れると選択肢の並びの後ろに並ぶ）。中身は名札の `Label`、text の `Label`、▼ の `Label`、選択肢の並びで、コンストラクタが作る。
`text` 属性は 1 ページだけを出す UXML での確認用。複数ページは `Show()` で渡す。

### 話者の渡し方

部品は本文を解析しない（本文から話者を取り出さない）。

| 場面 | 渡し方 | 名札 |
|---|---|---|
| 話者が別の欄にあるシナリオ | `Show(new Hone.MessageWindow.Page("ジョシュ", "あ、ハシゴだ。"), ...)` | ページごとに替わる |
| 地の文（話者なし） | `Page` の `speaker` を `null` か空文字列にする | 出ない |
| 話者を本文に混ぜる書き方（「＊「」など） | `Show("＊「...", ...)`（文字列のページ） | 一度も出ない |
| データには話者があるが、名札を出さない見た目 | `Page` で渡し、利用者の USS で名札を `display: none` にする（下の例） | 出ない |

名札を出す既定の規則は `.hone-message-window.is-speaker-set > .hone-message-window__speaker`（クラス 3 つ）なので、`.hone-message-window__speaker` だけの規則では詳細度で負けて隠れない。
隠すときは同じセレクターに範囲のクラスを足して書く（`.my-screen` は利用者が親に付けるクラス）:

```css
.my-screen .hone-message-window.is-speaker-set > .hone-message-window__speaker {
    display: none;
}
```

### ページごとの見た目

ページごとに文字色やフォントを変えたいときは、`Page` の `className` にクラス名を渡す。部品はそのページを出している間、窓にそのクラスを付け外しするだけで、色やフォントの値は持たない（値は利用者の USS が決める）。

- 次のページへ進むと、前のページのクラスを外してから新しいページのクラスを付ける。`Show` で出し直したときと、ページ無しにしたときも、前のクラスを外す。
- 値は検証しない。空白を含む値も 1 つのクラス名としてそのまま付く（USS では選べない）。複数のクラスを空白で区切って渡す使い方はできない。
- 本文は rich text として解釈しないので、タグでは色を変えられない。この口を使う。

```csharp
window.Show(
    new Hone.MessageWindow.Page(null, "（ふつうの文）"),
    new Hone.MessageWindow.Page(null, "ケツイ", "page-determination"));
```

```css
.my-screen .hone-message-window.page-determination .hone-message-window__text {
    color: rgb(255, 0, 0);
}
```

### 選択肢

ページの `choices` に文字列を 1 個か 2 個渡すと、そのページの全文が出たところで、選択肢ごとの `Hone.Button`（variant は `outline`）を本文の下に右寄せの横並びで出す。
押すと `choiceSelected` が選択肢の index で 1 回出て、そのあと `Advance()` と同じく次のページへ進むか、最後のページなら `completed` が出る。

- 選択肢を出している間（`is-choosing`）は、窓への Submit と、選択肢の外のクリックでは進まない（`Advance()` も何もしない）。選択肢の Button とその子要素への Submit とクリックでは、窓自身は進めない（Button の押下で選ばれ、その結果として 1 回だけ進む）。この間 `isChoosing` が true。▼ は出ない。
- 選択肢を出した時点で窓にフォーカスがあれば、最初の選択肢へフォーカスを移す（ゲームパッドで続けて選べる）。窓にフォーカスが無ければ移さない。
- `FocusScope` の trap はしない。Tab や 2D ナビゲーションで、フォーカスは窓自身や窓の外へも出る（窓にフォーカスが戻っても、Submit では選べない）。Cancel で選択肢を選ぶ動作も持たない。
- 選択肢を押した後は Button が消える。押す前にフォーカスが窓の中にあったなら、次の更新で、次のページの窓（次のページも選択肢なら最初の選択肢）へフォーカスを移す。`completed` の後と、`choiceSelected` の中で `Show` した後も同じ。`choiceSelected` か `completed` の中で利用者が別の要素へフォーカスを移していたら、移さない。
- 分岐は持たない。どの選択肢でどのページへ行くかは、利用者が `choiceSelected` を受けて `Show` し直す。`choiceSelected` の中で `Show` か `Advance()` をしたときは、部品はそれ以上進めない（`Show` なら新しい 1 ページ目がそのまま出る）。`choiceSelected` が呼ばれている間、`pageIndex` は選択肢のページを指す。
- `choices` が `null` のページは選択肢なし（Submit とクリックで送る）。空の配列は、何も押せないページになるので `Page` のコンストラクタが `ArgumentException` にする。上限は 2 個で、3 個以上も `ArgumentException`。`null` の要素は空文字列として出す。
- `Show` は `choices` の配列をコピーして持つ。`Show` の後に元の配列を書き換えても、出る選択肢は変わらない。
- 選択肢の Button は縮まず折り返さない（Button.uss）。幅が足りないときは並びが折り返して次の行に出る。長い文言を入れない。

```csharp
window.choiceSelected += index =>
{
    if (index == 0)
        window.Show("セーブしました。");
};
window.Show(new Hone.MessageWindow.Page(null, "旅の記録を 残していきますか？", choices: new[] { "はい", "いいえ" }));
window.Focus();
```

## API

| 名前 | 意味 |
|---|---|
| `text` | 属性。set は `Show(value)` と同じだが、空文字列と `null` はページ無しになる（`Show("")` は空のページが 1 つになり、`Advance()` で `completed` が出る）。get は今のページの文字列（ページが無ければ空文字列） |
| `charactersPerSecond` | 属性。文字送りの速さ。既定 0。ページの開始時に 0 以下（NaN と無限大も）なら即時表示。文字送りの途中で変えると、その時点から新しい速さで進む（出ている文字は減らない）。0 以下にすると全文が出る |
| `Page` | `MessageWindow` の中の型（`speaker` と `text` と `className` と `choices`）。`Page` は値を渡したまま持ち、`Show` が `choices` の配列をコピーする。`Page(speaker, text, className = null, choices = null)`。`Show` は、`speaker` が `null` か空文字列のページを話者なし、`text` が `null` のページを空文字列として扱う。`className` はそのページを出している間だけ窓に付くクラス名（上の「ページごとの見た目」）。`choices` は選択肢の文字列で、`null` か 1〜2 個（空の配列と 3 個以上は `ArgumentException`。上の「選択肢」） |
| `Show(params Page[] pages)` | 1 ページ目から出し直す。送りの途中でも最初からやり直す。`null` と空の配列はページ無し。`null` は型を付けて渡す（`Show((Page[])null)`。`Show(null)` と引数なしの `Show()` は 2 つの `Show` のどちらか決まらずコンパイルエラー） |
| `Show(params string[] pages)` | 話者なしのページとして `Show(params Page[])` と同じに扱う。`null` のページは空文字列 |
| `Advance()` | Submit とクリックと同じ処理（下の表） |
| `pageIndex` | 今のページ。ページが無いときは -1（読み取りのみ） |
| `pageCount` | ページ数（読み取りのみ） |
| `isChoosing` | 選択肢を出している間（`is-choosing` が付いている間）だけ true（読み取りのみ）。`choiceSelected` の中では選択肢を外した後なので false |
| `completed` | 最後のページで全文が出た後に `Advance()` されたとき 1 回出るイベント。最後のページの選択肢を押したときも出る。次の `Show` まで再び出ない |
| `choiceSelected` | 選択肢を押したとき 1 回出るイベント（`Action<int>`。引数は `choices` の index）。呼ばれている間、`pageIndex` は選択肢のページを指す。このあと次のページへ進むか `completed` が出る。この中で `Show` か `Advance()` をしたときは、部品はそれ以上進めない |

## Advance の処理

入力は `NavigationSubmitEvent`（フォーカスがあるとき）と `ClickEvent`（ウィンドウのどこでも）の 2 つだけ。どちらも `Advance()` を呼び、祖先には伝播させない。
選択肢の Button への Submit とクリックは、その選択肢を押したことになり、窓の `Advance()` は呼ばない（祖先にも伝播させない）。
押すたびに 1 歩進む。出ている途中なら全部出し、出きっていれば次へ。

| 今の状態 | Advance したとき |
|---|---|
| ページが無い（`Show` 前） | 何もしない |
| 文字送りの途中 | そのページの全文を出す（ページは進めない） |
| 選択肢を出している（`is-choosing`。下の 2 行より先に当てはまる） | 何もしない（選択肢を押すと進む） |
| 全文が出ていて、次のページがある | 次のページへ進み、文字送りを始める |
| 最後のページで全文が出ている（`completed` 前） | `completed` を 1 回出す |
| `completed` の後 | 何もしない（次の `Show` まで） |

## USS クラス

| クラス | 付く要素 |
|---|---|
| `hone-message-window` | MessageWindow 自身。`hone-focusable` も付く（Submit を受け取るためにフォーカスを取る。`tabIndex = 0`） |
| `hone-message-window__speaker` | 話者の名札の `Label`（先頭の子）。`is-speaker-set` の間だけ出る。文字送りしない。rich text として解釈しない。長い名前は省略記号で切る |
| `hone-message-window__text` | ページの文字列の `Label`。折り返す |
| `hone-message-window__indicator` | ▼ の `Label`。`is-waiting` の間だけ見える。`is-choosing` の間は行ごと詰める（`display: none`） |
| `hone-message-window__choices` | 選択肢の並び（最後の子）。選択肢の無いページでは子が無く、高さは 0 |
| `hone-message-window__choice` | 選択肢の `Hone.Button`（variant は `outline`）。`is-choosing` の間だけ並びの子にある |
| `is-speaker-set` | MessageWindow に付く状態クラス。今のページの `speaker` が `null` でも空文字列でもない間。名札の表示はこのクラスと USS で切り替わり、C# から `display` を書かない（利用者の USS で隠せなくなるため）。ページが無いときは付かない |
| ページの `className` | MessageWindow 自身に付く、利用者が `Page` で渡したクラス。そのページを出している間（文字送りの途中の `.is-revealing` の間も）付く。`null` か空文字列なら付かない。窓に既に付いているクラス（`hone-message-window` `hone-focusable`、状態クラスの `is-speaker-set` `is-revealing` `is-waiting` `is-choosing`、利用者が自分で付けたクラス）と同じ名前は使わない。ページが変わるとそのクラスごと外れる |
| `is-revealing` | MessageWindow に付く状態クラス。文字送りの途中の間 |
| `is-waiting` | MessageWindow に付く状態クラス。全文が出ていて、次のページがある間（▼ が見える）。選択肢を出している間は付かない |
| `is-choosing` | MessageWindow に付く状態クラス。選択肢を出していて、まだ押されていない間 |

最後のページで全文が出ているときは、`is-revealing` も `is-waiting` も付かない（選択肢のあるページなら `is-choosing` が付く）。
部品は演出を持たない。▼ の点滅や揺れ、送りの音、選択肢の決定の音は、これらの状態クラスとイベントを使って利用者が書く。

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
選択肢に `Hone.Button` を使うので、registry の依存に `Button` もある。選択肢へのフォーカスは移すだけで、`FocusScope` の trap は使わない。
