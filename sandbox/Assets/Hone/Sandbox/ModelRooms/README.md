# ModelRooms

既存のゲームの UI を Hone の部品で再現した「モデルルーム」の基盤。方針は AGENTS.md の「モデルルーム」節。

同じ構造（メッセージと はい / いいえ の Dialog）を、3 列に並べて見比べる。

| 列 | 内容 |
|---|---|
| 素 | registry の既定のまま。何も付けない |
| USS だけ | 舞台にルームの USS クラス（`.room-<genre>`）が付く |
| 注入あり | USS のクラスに加えて、`inject` が演出（Manipulator、スクリプト）を足す |

3 列は同じ中身で、左から順に手が入っている。構造は全ルーム共通で、ルームが変えるのは USS と演出のスクリプトだけ。

## 使い方

`ModelRooms.unity` を開いて Play する。上の dropdown（`Room`）でルームを選ぶと、2 列目と 3 列目にそのルームが当たり、3 列とも組み直されてメッセージが 1 ページ目から出る。

- メッセージを送る（Submit かクリック）と、最後のページで はい / いいえ の Dialog が開く
- はい / いいえ を選ぶと、見出しに結果が出て Dialog が閉じる
- 見出しの「もう一度」で、メッセージを最初から出す

ルームが 1 つも登録されていないときは、dropdown が「デフォルト」だけになり、3 列とも素（registry の既定）の見た目になる。

見出しは舞台の外にある。ルームのテーマは舞台（`.model-room-stage`）にだけ掛かる。

## ファイル

| ファイル | 内容 |
|---|---|
| `ModelRooms.unity` | `PanelRenderer` が 1 つ（Screen Space）。EventSystem は置かない |
| `ModelRoomsPanelSettings.asset` | `Sandbox/PanelSettings.asset` の複製。Theme Style Sheet だけ `ModelRoomsTheme.tss` |
| `ModelRoomsTheme.tss` | `Assets/Hone/HoneTheme.tss` と同じ `@import` に続けてルームの USS の `@import` を並べ、`:root`（HoneTheme.tss と同じ）はその後 |
| `ModelRooms.uxml` / `ModelRooms.uss` | 画面の枠（dropdown と 3 列）。sandbox の枠なので、幅や高さは実値で書いてある |
| `Message.uxml` / `YesNo.uxml` | 全ルーム共通の構造。特定のルームのためのクラスや要素は足さない |
| `ModelRoom.cs` | ルームの定義（`displayName` `className` `pages` `question` `yes` `no` `inject`） |
| `ModelRoomsController.cs` | 登録、列の組み立て、流れ |
| `Hone.Sandbox.ModelRooms.asmdef` | `Hone.Sandbox.UI` と `Hone.Core` を参照。`autoReferenced` は false。ルームのスクリプトもこのアセンブリに入る |
| `ClassicJrpg/` | クラシック JRPG 風のルーム。`ClassicJrpg.uss`（段階 2）、`ClassicJrpgRoom.cs`（登録と段階 3 の `inject`）、`Cursor.png`（自作の ▶）。フォントは `Sandbox/Fonts/` の M PLUS Rounded 1c |
| `TextAdventure/` | テキストアドベンチャー風のルーム。`TextAdventure.uss`（段階 2）、`TextAdventureRoom.cs`（登録と段階 3 の `inject`）、`Nameplate.svg`（自作の名札の背景）と `WindowFrame.svg`（自作の窓の枠と地）、それぞれを変換した PNG。☞ は `Sandbox/Icons/` の Tabler Icons。フォントは `Sandbox/Fonts/` の DotGothic16（本文は 28px 用の `DotGothic16-28.asset`、名札は `DotGothic16.asset`。Standard の生成器で描く） |
| `RetroIndieRpg/` | レトロインディー RPG 風のルーム。`RetroIndieRpg.uss`（段階 2）、`RetroIndieRpgRoom.cs`（登録と段階 3 の `inject`）、自作のドット絵（`IconYes.png` の ○、`IconNo.png` の ×、`Heart.png`。8×7 を 2 倍で描くので、import の Filter Mode は Point）。フォントは `Sandbox/Fonts/` の JF ドット東雲ゴシック 14（14px 用の `JF-Dot-Shinonome14.asset`。参考が使うものと同じ書体。Standard の生成器で描く） |

`HoneTheme.tss` を変えたら（コンポーネントの追加、フォントの変更）、`ModelRoomsTheme.tss` の `@import` と `:root` も同じに直す。`HoneTheme.tss` にルームの `@import` は足さない（Gallery とテストが使うテーマを汚すため）。

## ルームの足し方

1. `ModelRooms/<Genre>/` を作る（`<Genre>` は PascalCase のジャンル名。実在のタイトル名・会社名は使わない）
2. 中に USS を置く。ルームの USS は、舞台に付く `.room-<genre>`（kebab-case）で範囲を絞る。トークンで表せる値は、範囲の先頭でトークンを上書きする。トークンで表せない値だけを、部品の規則の上書きに書く
3. `ModelRoomsTheme.tss` に、そのルームの USS の `@import` を足す（`:root` の前。このファイルからの相対パス）
4. 演出のスクリプトは同じディレクトリに置く（`Hone.Sandbox.ModelRooms` アセンブリに入る）
5. sandbox 側のファイルから `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` で `ModelRoomsController.Register` を呼ぶ

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void Register()
{
    ModelRoomsController.Register(new ModelRoom
    {
        displayName = "<ジャンル名> 風",
        className = "room-<genre>",
        pages = new[] { new MessageWindow.Page(null, "1 ページ目"), new MessageWindow.Page("話者", "2 ページ目") },
        question = "問い",   // 省略すると YesNo.uxml の文面のまま
        yes = "はい",
        no = "いいえ",
        inject = stage => { /* 3 列目の舞台に演出を足す */ },
    });
}
```

- `pages` はメッセージの文面（`MessageWindow.Page`。話者は `speaker` で渡すと名札に出る。null なら名札なし）。ルームを選ぶと 3 列とも（素の列も）この文面になる（見比べるのは見た目なので、文面は揃える）。null か空なら既定の文面。
  そのゲームらしい、キャラクター個人のものではない印象的なセリフにする。話者の名前に原作のキャラクター名は使わない。送り待ちの ▼ を見せるため、2 ページ以上にする
- `question` `yes` `no` は、はい / いいえ の Dialog の問い（`.hone-dialog__description` の Label）とボタンの文面。null のものは `YesNo.uxml` の文面のまま。`pages` と同じく 3 列とも差し替わる（UXML は変えない）。見出しの結果には押したボタンの文面が出る
- 同じ `className` の再登録は置き換わる。dropdown には登録順に並ぶ
- `displayName` と `className` は空にできない（`Register` が例外を投げる）。`className` は登録の識別子なので、`Register` の後で変えない
- `inject` は 3 列目の舞台が panel に attach された後に、組むたびに 1 回呼ばれる。メッセージの `Show` は `inject` の後なので、`inject` で `MessageWindow` の設定（`charactersPerSecond` など）を変えれば 1 ページ目から効く
- このプロジェクトは Domain Reload を無効にしているので、`Register` した内容は Play をまたいで残る。テストで一時的に登録したものは `Unregister(className)` で消す
- dropdown は UI が読み込まれた時点の登録内容で作る。それより後の `Register` は dropdown に出ない
- 登録した後も、registry の既定の見た目にルームの見た目を持ち込まない。2 つ以上のルームに同じ規則の上書きが出たらトークンの候補、同じ画面の型が出たら block の候補にする
- ルームの USS は theme（`ModelRoomsTheme.tss`）から読まれるので、`ModelRooms.uss`（`ModelRooms.uxml` の Style）の規則には、詳細度を上げても勝てない（舞台の `padding` で確認。6000.7.0b2）。
  舞台の枠（`.model-room-stage` の `padding` と枠線）はルームから変えられない前提で置く。`ModelRooms.uss` が書いていないプロパティ（舞台の `background-color` など）は、ルームの USS で付けられる
- 窓（メッセージと Dialog）は、舞台の中の層（`.model-room-layer`）にまとめて入る。層は舞台の余白の内側を埋め、Dialog も層の全面を覆うので、メッセージと Dialog の位置は同じ基準で測れる。
  窓の共通の親に掛けたい規則（窓をまとめて半透明にするなど）は `.room-<genre> .model-room-layer` に書く
- 窓を半透明にするときは、窓 1 枚ずつではなく、層に `filter: opacity()` を掛ける。親の filter は子をまとめて描いてから半透明にするので、重なった下の窓が上の窓から透けない（窓 1 枚ずつだと、重なった部分で下の窓の枠が透け、地も二重に暗くなる）。
  文字と枠も同じ割合で薄くなる（6000.7.0b2、Screen Space で確認。World Space は未確認）

## 差分の表

ルームごとに、素から手を入れたときに分かったことを 1 行ずつ足す。各ルームの Issue が書く。

| ルーム | 段階 2 で部品の規則を上書きした箇所（トークンの候補） | 構造を変えたかった点 | 注入で足りなかった口 | 他のルームと共通の型 |
|---|---|---|---|---|
| クラシック JRPG 風（`ClassicJrpg/`） | 窓の層: 窓を不透明に描き、層ごと半透明にする（`.model-room-layer` の `filter: opacity(0.8)`）。窓の外側の暗い縁取り（`filter: drop-shadow`）。<br>MessageWindow: 幅・高さ・下寄せの位置・内側の余白（`width` `height` `margin-bottom` `align-self` `padding`）。本文の空白をそのまま出す（`white-space: pre-wrap`。文面の語の区切りの全角の空白）。<br>▼: 下の枠の中央に重ねる（`position: absolute` `left` `bottom` `translate`）、字の半分の大きさ（`font-size`）、Label の余白を消す（`padding` `margin`）、点滅の `transition`。<br>Dialog: content を下に寄せる（`.hone-dialog` の `justify-content`）。content をメッセージの上の右寄せに置く（`margin-bottom` `translate`）、幅（`width`）、内側の余白（`padding`）。footer を縦並び・左寄せ（`flex-direction` `align-items` `flex-wrap` `margin-top`）、footer の子の `margin-left` を消す。<br>Button: 塗りと枠を消す（`background-color` `border-width`）、文字の左寄せと余白（`-unity-text-align` `padding`）、`:hover` `:active` の `opacity`。<br>カーソル: フォーカスした Button の左の余白に `background-image`（▶）、揺れの `transition`（`background-position-x`）。Button の ring を消す（`border-width: 0`）。<br>舞台の背景色（sandbox の見た目。参考の 3D の画面の代わり） | 質問文を はい / いいえ の窓ではなくメッセージ側に出したかった（Dialog の description を `display: none` で隠した）。<br>はい / いいえ の窓を、メッセージの窓を基準に置きたかった（Dialog は親の全面を覆うだけで、ほかの要素を基準に置く手段が無い。メッセージの大きさから逆算した `translate` と `margin-bottom` の数値で合わせたので、メッセージの大きさを変えると黙ってずれる）。<br>窓をまとめる親が要った（窓を層ごと半透明にするため。共通の UXML ではなく、基盤が舞台の中に `.model-room-layer` を足した） | 演出の注入ではなし（文字送りは `charactersPerSecond`、確定の間は `commitDelay` と `.is-holding`、▼ は `.is-waiting`、▶ は `:focus` で書けた）。<br>参考にあり、このルームの構造では再現しない振る舞い: 行があふれると上へスクロールする（MessageWindow にページ内のスクロールが無い）、子メニューを開いても親メニューのカーソルが灰色で残る（フォーカスを失った選択を表す状態クラスが無い） | |
| テキストアドベンチャー風（`TextAdventure/`） | MessageWindow: 幅・高さ・下寄せの位置・内側の余白（`width` `height` `margin-bottom` `align-self` `padding`）。地を半透明の黒にする（`background-color`。Dialog に窓の地が無く窓が重ならないので、#60 の層ごとの `filter: opacity()` は使わない）。改行と全角の空白をそのまま出す（`white-space: pre-wrap`）。<br>名札（#78 の `.hone-message-window__speaker`）: 窓の左上の縁の外に置く（`position: absolute` `left` `top` `height`）、自作画像の 9-slice（`background-image` `-unity-slice-*`）、余白と文字の寄せ（`padding` `margin` `-unity-text-align`）。<br>▼: 下の枠の中央（`position: absolute` `left` `bottom` `translate`）、大きさ（`font-size`）、Label の余白を消す（`padding` `margin`）。<br>Dialog: content を上に寄せる（`.hone-dialog` の `justify-content`）、content の地と枠を消す（`background-color` `border-width`）、幅と余白（`width` `max-width` `padding`）。問いの色・大きさ・改行（`color` `font-size` `white-space` `margin-left`）。footer を縦並び・左寄せ（`flex-direction` `align-items` `flex-wrap` `margin-top`）、footer の子の `margin-left` を消す。<br>Button: 塗りと枠を消す（`background-color` `border-width`）、高さ・余白・文字の大きさと左寄せ（`height` `padding` `font-size` `-unity-text-align`）、`:hover` `:active` の `opacity`、確定の間（`.is-holding`）の `color`。<br>カーソル: フォーカスした Button の左の余白に `background-image`（☞。SVG を Texture2D で import）。Button の ring を消す（`border-width: 0`）。<br>舞台の背景色（sandbox の見た目。参考の法廷の絵の代わり） | 選択肢の場面でメッセージの窓と名札を隠したかった（参考は選択肢の場面で窓が消える。Dialog が開いていることを兄弟の窓に伝える状態クラスが無く、USS では隠せない。問いと選択肢を名札より上に詰めて重ならないようにした）。<br>選択肢の文面（問い、はい、いいえ）をルームから変えたかった（共通の構造の文面が固定。基盤の `ModelRoom` に `question` `yes` `no` を足した。registry の口ではない） | 演出の注入ではなし（文字送りは `charactersPerSecond`、確定の間は `commitDelay` と `.is-holding`、▼ は `.is-waiting`、☞ は `:focus`、名札は #78 の `MessageWindow.Page` で書けた）。<br>参考にあり、このルームでは再現しない振る舞い: メッセージのページごとに文字色を変える（心の声の水色。このルームでは Dialog の問いだけなので USS で付けられたが、メッセージのページごとに色を渡す口は無い） | クラシック JRPG 風と共通（2 ルームに出たので、トークンか block の候補）: MessageWindow の幅・高さ・下寄せ・余白の上書き、`white-space: pre-wrap`、▼ を下の枠の中央に absolute で置く、▼ の点滅を舞台の状態クラス（`.is-blink-off`）で付ける、Dialog の footer を縦並び・左寄せにする、Button の塗り・枠・ring を消して `:hover` `:active` の `opacity` を戻す、フォーカスした Button の左に `background-image` のカーソル、舞台の背景色。<br>画面の型としては「メッセージの窓 + 文字だけの縦並びの選択肢 + カーソル」が 2 ルームで同じ（block の候補） |
| レトロインディー RPG 風（`RetroIndieRpg/`） | MessageWindow: 幅・高さ・下寄せの位置・内側の余白（`width` `height` `margin-bottom` `align-self` `padding`）。本文の空白をそのまま出す（`white-space: pre-wrap`。語の区切りと、改行した行の頭の字下げの全角の空白）。<br>▼: 枠の中の右下に重ねる（`position: absolute` `right` `bottom`）、Label の余白を消す（`padding` `margin`）。<br>Dialog: 問いを隠す（`.hone-dialog__description` の `display: none`）。content を下に寄せる（`.hone-dialog` の `justify-content`）、content の幅・下の余白・内側の余白（`width` `max-width` `margin-bottom` `padding`）、content の地と枠を消す（`background-color` `border-width`）。footer を横並び・左寄せ（`flex-direction` `flex-wrap` `justify-content` `margin-top`）、footer の子の `margin-left` を消して `margin-right` で間を空ける。<br>Button: 高さ・余白・左寄せ（`height` `padding` `-unity-text-align`）、塗りを消す（`background-color`）、枠の太さと色・文字色（`border-width` `border-color` `color`）、`:hover` `:active` の `opacity`。フォーカスで枠と文字を黄色にする（ring は使わない）。<br>アイコン: はい / いいえ の左に自作のドット絵（`#yes` `#no` の `background-image` `background-position-*` `background-repeat` `background-size`）。フォーカスでハートに差し替える（`:focus` の `background-image`）。<br>舞台の背景色（sandbox の見た目。参考の黒い画面） | 問いの文を出さず、ボタンだけを並べたかった（参考の選択肢には問いの文が無い。Dialog の description を `display: none` で隠した）。<br>ボタンごとに違うアイコンを付けたかった。Button に slot が無くても、アイコンは `background-image` で描けた。ただし、共通の構造では はい / いいえ に付くクラスが同じ（variant の違いだけ）なので、名前（`#yes` `#no`）の ID セレクタで書くことになり、テーマの規則がボタンの名前に依存した。アイコンはボタンごとの中身なので、テーマではなく構造の側で持ちたい（Button にアイコンを渡す口があれば、USS は差し替えの規則だけで済む）。<br>アイコンとフォーカスのカーソルは同じ `background-image` を取り合うので、アイコンを残したままカーソルを足すことはできない（このルームは差し替えなので足りた） | 演出の注入ではなし（文字送りは `charactersPerSecond`、確定の間は `commitDelay` と `.is-holding`、ハートの揺れは `:focus` と舞台の状態クラス `.is-sway`、▼ は `.is-waiting` で書けた）。<br>参考にあり、このルームでは再現しない振る舞い: ページごとに文字色を変える（1 語だけを赤で出すページ）、ページごとにフォントを切り替える（話し手ごとに崩した書体で話す）。`MessageWindow.Page` は話者と本文しか持たず、本文は `<noparse>` で囲んで出すのでリッチテキストのタグも使えない。テキストアドベンチャー風の「ページごとに文字色を変える口が無い」と同じ不足 | クラシック JRPG 風・テキストアドベンチャー風と共通（3 ルームに出た）: MessageWindow の幅・高さ・下寄せ・余白の上書き、`white-space: pre-wrap`、▼ を absolute で置く、Dialog の footer の並びを変えて子の `margin-left` を消す、Button の塗りを消して `:hover` `:active` の `opacity` を戻す、フォーカスの見た目を ring 以外で付ける（このルームは枠を残し、`:focus` で枠と文字の色を変える。ほかの 2 ルームは枠と ring を消す）、フォーカスした Button に `background-image`、舞台の背景色。<br>クラシック JRPG 風と共通: Dialog の問い（description）を `display: none` で隠す。<br>テキストアドベンチャー風と共通（2 ルームに出たので、口の候補）: ページごとの見た目（文字色、このルームではフォントも）を変える口が無い。<br>画面の型としては「メッセージの窓 + 選択肢 + カーソル」が 3 ルームで同じ。このルームだけ選択肢が窓の外の横並び |

## 撮影

Gallery の Screen Space の手順（`sandbox/README.md` の「Gallery」の撮影手順）と同じで、シーンが `ModelRooms.unity` になるだけ。Editor は `sandbox/README.md` の「Editor の開き方」で開いておく。

3 列とも Dialog が開いた状態で撮る。各列のメッセージを最後まで送ってから撮る。送りは eval から各列の `MessageWindow.Advance()` を呼んでよい。ルームは `ModelRoomsController.SelectRoom(className)` で切り替える（dropdown の代わりに、eval から切り替えて撮るための入口）。

```bash
unity command open_scene --path Assets/Hone/Sandbox/ModelRooms/ModelRooms.unity --project-path <sandbox の絶対パス>
unity command editor_play --project-path <sandbox の絶対パス>
# ルームを選ぶときだけ。ルームの無い基盤だけを撮るときは飛ばす
unity command eval_file --file <select-room.cs の絶対パス> --project-path <sandbox の絶対パス>
unity command eval_file --file <advance.cs の絶対パス> --project-path <sandbox の絶対パス>
MSYS_NO_PATHCONV=1 unity command capture_game_view --source screen --width 1920 --height 1080 --format json --project-path <sandbox の絶対パス> > <json の絶対パス>
unity command editor_stop --project-path <sandbox の絶対パス>
```

`capture_game_view` の base64 を書き出す手順と注意点（`--save_path` を渡さない、`screenshot --view game` を使わない）は Gallery の撮影手順と同じ。

ルームの切り替え（`select-room.cs`。`"room-classic-jrpg"` を登録した `className` に替える）:

```csharp
var controllers = UnityEngine.Object.FindObjectsByType<Hone.Sandbox.ModelRooms.ModelRoomsController>();
if (controllers.Length == 0)
    throw new System.InvalidOperationException("no active ModelRoomsController (is Play Mode on and a panel enabled?)");
foreach (var c in controllers)
    c.SelectRoom("room-classic-jrpg");
return controllers.Length;
```

- `SelectRoom` は dropdown の `index` を変えて ChangeEvent を出す。すでに選ばれている `className` を渡すと何も起きない。切り替えると 3 列が組み直され、メッセージは 1 ページ目に戻る。切り替えてから `advance.cs` を実行する
- 切り替えの中（組み直しと `inject`）で起きた例外は、ChangeEvent の callback の中で出るので eval には返らない。撮る前に Console に ModelRooms のエラーが出ていないことを確かめる

メッセージを最後まで送る（`advance.cs`。3 列とも Dialog が開く）:

```csharp
var controllers = UnityEngine.Object.FindObjectsByType<Hone.Sandbox.ModelRooms.ModelRoomsController>();
if (controllers.Length == 0)
    throw new System.InvalidOperationException("no active ModelRoomsController (is Play Mode on and a panel enabled?)");
foreach (var c in controllers)
    for (var i = 0; i < c.columns.childCount; i++)
    {
        var window = UnityEngine.UIElements.UQueryExtensions.Q<Hone.MessageWindow>(c.columns[i], (string)null, (string)null);
        for (var n = 0; n < window.pageCount * 2; n++)
            window.Advance();
    }
return controllers.Length;
```

- Editor のウィンドウが背面にあると、Play のフレームが進まず、文字送りや点滅が止まったまま、送った後の画面も描き直されない（6000.7.0b2）。
  Play に入った後、eval から `UnityEngine.Application.runInBackground = true;` を実行してから撮る（実行中だけ効き、プロジェクト設定は変わらない）
- フォーカスで変わる見た目（カーソルなど）は、panel のフォーカスが 1 つなので、写したい列の Button に eval から `Focus()` を当ててから、列ごとに撮る
- 回数はページ数の 2 倍。文字送りの速さ（`charactersPerSecond`）が 0 より大きいルームでは、ページごとに `Advance()` の 1 回目が文字送りの完了に使われるため。`completed` の後の `Advance()` は何もしないので、即時表示のルームで余っても害はない
- eval の本文は `using` を書けず、`Query` などの拡張メソッドは `UQueryExtensions` を通して呼ぶ（`c.columns.Query<...>()` と書くとコンパイルに失敗する。6000.7.0b2）
