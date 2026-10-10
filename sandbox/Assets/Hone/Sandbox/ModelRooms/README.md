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
| `ModelRoom.cs` | ルームの定義（`displayName` `className` `inject`） |
| `ModelRoomsController.cs` | 登録、列の組み立て、流れ |
| `Hone.Sandbox.ModelRooms.asmdef` | `Hone.Sandbox.UI` と `Hone.Core` を参照。`autoReferenced` は false。ルームのスクリプトもこのアセンブリに入る |
| `ClassicJrpg/` | クラシック JRPG 風のルーム。`ClassicJrpg.uss`（段階 2）、`ClassicJrpgRoom.cs`（登録と段階 3 の `inject`）、`Cursor.png`（自作の ▶）。フォントは `Sandbox/Fonts/` の M PLUS Rounded 1c |

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
        displayName = "クラシック JRPG 風",
        className = "room-classic-jrpg",
        inject = stage => { /* 3 列目の舞台に演出を足す */ },
    });
}
```

- 同じ `className` の再登録は置き換わる。dropdown には登録順に並ぶ
- `displayName` と `className` は空にできない（`Register` が例外を投げる）。`className` は登録の識別子なので、`Register` の後で変えない
- `inject` は 3 列目の舞台が panel に attach された後に、組むたびに 1 回呼ばれる。メッセージの `Show` は `inject` の後なので、`inject` で `MessageWindow` の設定（`charactersPerSecond` など）を変えれば 1 ページ目から効く
- このプロジェクトは Domain Reload を無効にしているので、`Register` した内容は Play をまたいで残る。テストで一時的に登録したものは `Unregister(className)` で消す
- dropdown は UI が読み込まれた時点の登録内容で作る。それより後の `Register` は dropdown に出ない
- 登録した後も、registry の既定の見た目にルームの見た目を持ち込まない。2 つ以上のルームに同じ規則の上書きが出たらトークンの候補、同じ画面の型が出たら block の候補にする
- ルームの USS は theme（`ModelRoomsTheme.tss`）から読まれるので、`ModelRooms.uss`（`ModelRooms.uxml` の Style）の規則には、詳細度を上げても勝てない（舞台の `padding` で確認。6000.7.0b2）。
  舞台の枠（`.model-room-stage` の余白 16px と枠線）はルームから変えられない前提で置く。`ModelRooms.uss` が書いていないプロパティ（舞台の `background-color` など）は、ルームの USS で付けられる
- メッセージは舞台の余白の内側に並ぶが、Dialog は absolute なので余白を含めた舞台の全面を覆う。両者の位置を合わせるときは、Dialog 側に舞台の余白 16px を足す

## 差分の表

ルームごとに、素から手を入れたときに分かったことを 1 行ずつ足す。各ルームの Issue が書く。

| ルーム | 段階 2 で部品の規則を上書きした箇所（トークンの候補） | 構造を変えたかった点 | 注入で足りなかった口 | 他のルームと共通の型 |
|---|---|---|---|---|
| クラシック JRPG 風（`ClassicJrpg/`） | MessageWindow: 幅・高さ・下寄せの位置・内側の余白（`width` `height` `margin-bottom` `align-self` `padding`）。<br>▼: 下の枠の中央に重ねる（`position: absolute` `left` `bottom` `translate`）、字の半分の大きさ（`font-size`）。<br>Dialog: content をメッセージの上の右寄せに置く（`justify-content` `margin-bottom` `translate`）、幅（`width`）、内側の余白（`padding`）。footer を縦並び・左寄せ（`flex-direction` `align-items` `flex-wrap` `margin`）。<br>Button: 塗りと枠を消す（`background-color` `border-width`）、文字の左寄せと余白（`-unity-text-align` `padding`）、`:hover` `:active` の `opacity`。<br>カーソル: フォーカスした Button の `background-image`（▶）。窓の枠に半分重ねるため Button の `margin-left` を負にする。Button の ring を消す（`border-width: 0`）。<br>舞台の背景色（sandbox の見た目。参考の 3D の画面の代わり） | 質問文を はい / いいえ の窓ではなくメッセージ側に出したかった（Dialog の description を `display: none` で隠した）。<br>▶ を窓の枠にまたがる位置に、Button の外の要素として置きたかった（背景画像は Button の箱の外に描けないので、Button の箱を負の margin で窓の外へ出した） | 演出の注入ではなし（文字送りは `charactersPerSecond`、確定の間は `commitDelay` と `.is-holding`、▼ は `.is-waiting`、▶ は `:focus` で書けた）。<br>参考にあり、このルームの構造では再現しない振る舞い: 行があふれると上へスクロールする（MessageWindow にページ内のスクロールが無い）、子メニューを開いても親メニューのカーソルが灰色で残る（フォーカスを失った選択を表す状態クラスが無い） | |

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

- フォーカスで変わる見た目（カーソルなど）は、panel のフォーカスが 1 つなので、写したい列の Button に eval から `Focus()` を当ててから、列ごとに撮る
- 回数はページ数の 2 倍。文字送りの速さ（`charactersPerSecond`）が 0 より大きいルームでは、ページごとに `Advance()` の 1 回目が文字送りの完了に使われるため。`completed` の後の `Advance()` は何もしないので、即時表示のルームで余っても害はない
- eval の本文は `using` を書けず、`Query` などの拡張メソッドは `UQueryExtensions` を通して呼ぶ（`c.columns.Query<...>()` と書くとコンパイルに失敗する。6000.7.0b2）
