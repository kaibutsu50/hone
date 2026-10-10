# sandbox

Hone の検証用 Unity プロジェクト。コンポーネントの動作確認と、多言語スクリーンショットの撮影に使う。
テンプレートは URP（`backdrop-filter` が URP 限定のため）。

## Editor

| 項目 | 値 |
|---|---|
| Unity | 6000.7.0b2 (d6546dc2b3a9) |
| 基準 | Unity 6.7 LTS が出るまでは 6000.7 系の最新 alpha / beta を使う |

`ProjectSettings/ProjectVersion.txt` が正。

## 構成

`Assets/Hone/` 配下のみ示す。`Assets/Scenes/` `Assets/Settings/` `Assets/TutorialInfo/` などの URP テンプレート由来のアセットは省略している。

```
Assets/Hone/
  Core/               registry/Core の手コピー
    Runtime/          Hone.Core.asmdef、Core.uss、BackStack.cs（NavigationCancel で最前面の IDismissable を閉じる）、IDismissable.cs、
                      FocusScope.cs（subtree 限定のナビ、trap、初期フォーカスと復元）
    Editor/           Hone.Core.Editor.asmdef、HoneSync.cs（メニュー `Hone > Sync`）
  UI/                 registry/UI の手コピー
    Hone.Sandbox.UI.asmdef  sandbox 専用（registry には置かない）。UI には asmdef を置かない規約なので、Gallery と PlayMode テストから参照できるよう、
                      利用者が自分の asmdef に UI を乗せる形をここで再現する。Hone.Core を参照し（Dialog.cs が FocusScope と BackStack を使う）、autoReferenced は false（Assembly-CSharp の Experiments に Hone.Button を見せない）
    Button/           Hone.Button（Button.cs、Button.uxml、Button.uss、README.md）
    Dialog/           Hone.Dialog（Dialog.cs、Dialog.uxml、Dialog.uss、README.md）
    MessageWindow/    Hone.MessageWindow（MessageWindow.cs、MessageWindow.uxml、MessageWindow.uss、README.md）
  Tokens.uss          registry/Tokens.uss の手コピー
  HoneTheme.tss       registry/HoneTheme.tss の手コピー。差分は、`hone add` が挿入する各コンポーネントの USS の @import（Button、Dialog、MessageWindow）と、末尾の :root（--hone-font-body に Sandbox/Fonts/RobotoMono.asset を指定）だけ。
                      利用者が雛形のコメントに従って自分で書く内容に当たる。registry 側を変えたら、この @import と :root を残して同期する
  hone.manifest.json  `hone init` が作る manifest の手書き（`fonts` は空）。`Hone > Sync` が読む。`hone add` が追記する `components` は、Sync が読まないので書いていない
  HonePanelTextSettings.asset   `Hone > Sync` が作った PanelTextSettings（Fallback Font Assets は空）
  Sandbox/            sandbox 固有のアセット。`hone add` がコピーする領域と混ぜない
    Sandbox.unity     PanelRenderer を 1 つ置いたシーン（EventSystem は置かない）
    Sandbox.uxml      空の UXML
    PanelSettings.asset   Theme Style Sheet に HoneTheme.tss、Text Settings に HonePanelTextSettings.asset を割り当て済み（Screen Space）
    WorldSpacePanelSettings.asset   PanelSettings.asset の複製で、Render Mode だけ World Space にしたもの。Theme Style Sheet と Text Settings は同じで、scale mode と pixels per unit は Unity の既定のまま
    WorldSpacePanelSettings2.asset  WorldSpacePanelSettings.asset の複製（中身は同じ）。同じ asset を 2 枚の PanelRenderer に割り当てると同じ panel になるので、WorldSpace/ の 2 枚目の panel 用に分けてある
    Fonts/            検証用フォント。RobotoMono-Regular.ttf（Apache-2.0、Unity Editor 同梱）とその LICENSE、そこから作った Dynamic の FontAsset。
                      DotGothic16-Regular.ttf（SIL OFL 1.1、google/fonts の ofl/dotgothic16）とその OFL.txt、そこから作った Dynamic の DotGothic16.asset
                      （「RoomExp の検証」の (c) の推奨値。RASTER_HINTED、sampling 16、padding 1、atlas の Filter Mode は Point、multi atlas は無効）と、
                      28px で直接ラスタライズする DotGothic16-28.asset（sampling 28。ほかは同じ。行の高さを 35.5 に変えてある。ModelRooms のテキストアドベンチャー風が使う。「既知の事項」）。
                      MPLUSRounded1c-Medium.ttf（SIL OFL 1.1、google/fonts の ofl/mplusrounded1c）とその MPLUSRounded1c-OFL.txt、そこから作った Dynamic の MPLUSRounded1c.asset
                      （Hone > Sync と同じ設定。SDFAA、sampling 90、padding 9、multi atlas 有効）。ModelRooms のクラシック JRPG 風が使う。
                      取得した時点（2026-10）の google/fonts のこのフォルダには OFL.txt が無かったので、MPLUSRounded1c-OFL.txt は、フォントの name table と METADATA.pb の著作権表記に、OFL 1.1 の本文（OFL.txt と同じ）を付けたもの
    Experiments/<Name>/   Issue ごとの検証。FontVar/ は `-unity-font-definition` を USS 変数経由で差し替えられるかの検証
                      FontVar/Resources/Fonts/ は case 3（`resource()`）用の Fonts/RobotoMono.asset の複製。元を作り直したら同期する。
                      Resources 配下なので sandbox のすべての Player ビルドに入る
                      FocusTrap/ は Dialog の focus trap 機構（IgnoreEvent の同 frame 順序、外側へのフォーカス漏れ、フォーカスが無いときの方向入力、EventSystem の有無）の検証
                      TokensExp/ は --hone-font-body（theme の :root）と .hone-text、.hone-focusable の ring の検証（Player の検証を含む）
                      ThemeExp/ は .tss の @import（絶対パス、相対パス）、既定テーマの Button の :focus、既定フォントの FontAsset と日本語の描画の検証
                      WorldSpace/ は World Space の panel に、カメラ経由の入力（PanelInputConfiguration + EventSystem）が届くかと、Hone.Button / Hone.Dialog が World Space の panel で動くかの検証。
                      Hone.Button と Hone.Dialog を型で扱うので、この中にだけ asmdef（Hone.Sandbox.Experiments.WorldSpace.asmdef。Hone.Sandbox.UI を参照し、autoReferenced は false）を置く
                      RoomExp/ はモデルルームの前提（:focus の背景画像のカーソル、ループするアニメーション、ピクセルフォント、範囲を絞ったフォント、文字送り）の検証。
                      RoomExp.tss は RoomExp 専用の theme で、RoomExp.uss を @import する（HoneTheme.tss は変えない）。RoomExp/Fonts/ は (c) の比較用の FontAsset
    Gallery/          多言語スクリーンショットの撮影基盤。TestStrings.json（スクリプトごとの表示名・短文・長文）、Gallery.unity（PanelRenderer が 2 枚。Screen Space の `PanelRenderer` と World Space の `PanelRendererWorldSpace`）/ Gallery.uxml / Gallery.uss、GalleryController（左のリストで選んだ 1 コンポーネントを右に出す）、
                      GalleryHoneEntries（Hone.Button、Core.uss のクラスを付けた列、開いた状態の Hone.Dialog、2 ページの 1 ページ目を出した Hone.MessageWindow を登録する）、GalleryFocus（ring を写すため、読み込み時と言語の切り替え時に最初の .hone-focusable にフォーカスを当てる。撮影用に、eval から同じ当て直しを呼ぶ `RefocusLater` を持つ）、
                      Hone.Sandbox.Gallery.asmdef（Tests/PlayMode が参照する。Hone.Sandbox.UI を参照する）
    ModelRooms/       モデルルームの基盤。同じ構造（はい / いいえ の Dialog とメッセージ）を「素」「USS だけ」「注入あり」の 3 列に並べ、dropdown で選んだルームを 2 列目と 3 列目に当てる。ModelRooms.unity（Screen Space の PanelRenderer が 1 枚）/ ModelRooms.uxml / ModelRooms.uss、
                      Message.uxml と YesNo.uxml（全ルーム共通の構造）、ModelRoom（ルームの定義）、ModelRoomsController（登録、列の組み立て、流れ）、ModelRoomsTheme.tss（ModelRooms 専用の theme。HoneTheme.tss と同じ @import に、ルームの USS の @import を足す）、
                      ModelRoomsPanelSettings.asset（PanelSettings.asset の複製で、Theme Style Sheet だけ ModelRoomsTheme.tss）、Hone.Sandbox.ModelRooms.asmdef（Tests/PlayMode が参照する。Hone.Sandbox.UI と Hone.Core を参照し、autoReferenced は false）。
                      ClassicJrpg/ はクラシック JRPG 風のルーム（ClassicJrpg.uss、ClassicJrpgRoom.cs、Cursor.png）。
                      TextAdventure/ はテキストアドベンチャー風のルーム（TextAdventure.uss、TextAdventureRoom.cs、名札の背景の Nameplate.svg と窓の枠と地の WindowFrame.svg、それぞれを ImageMagick で変換した PNG）。フォントは本文が DotGothic16-28.asset の 28px、名札が DotGothic16.asset の 16px。使い方は ModelRooms/README.md
    Icons/            検証用アイコン。hand-finger-right.svg（Tabler Icons、MIT。線の色だけ白に改変）とその TablerIcons-LICENSE.txt。
                      Texture2D として import する（.meta の svgType: 2）。理由は「既知の事項」の SVG の項。ModelRooms のテキストアドベンチャー風が使う
    Tests/EditMode/   EditMode テスト。HoneSyncTests（`Assets/HoneSyncTestsTmp/` に manifest と PanelSettings を作って `HoneSync.Run` を呼び、終わったら消す）
    Tests/PlayMode/   PlayMode テスト。BackStackTests、FocusScopeTests、DialogTests、MessageWindowTests（ページの無い状態の 1 件を除く）、ButtonTests の 1 件（NavigationSubmitEvent）は PanelRenderer を GameObject で作って panel を得る（`UNITY_EDITOR` のときだけコンパイルされる。Editor で実行する）。
                      DialogTests の `_WorldSpace` が付く 2 件（初期フォーカスと overlay の PointerDown）だけは、World Space の panel（`CreateWorldSpacePanel`）でも回す。`SendEvent` で送るので入力経路は通らない
                      ModelRoomsTests は、ModelRooms.unity を additive で読み込んで ModelRoomsController が組んだ 3 列を操作する（Screen Space のみ）
```

`registry/` の内容は手でコピーしている。`hone add` ができたら CLI に置き換える。
シンボリックリンクや `file:` 参照では取り込まない。

## パッケージ

- Input System: 6.7 では core package（`com.unity.inputsystem` 6.7.0）
- Test Framework 1.9.0、UI Test Framework 6.7.0（`com.unity.ui.test-framework`）。どちらも Editor 同梱
- `com.unity.pipeline` 0.7.0-exp.1: `unity command` で Editor を操作するため。**0.8.0-exp.1 にしない。**
  0.8 は `[CliCommand]` を別アセンブリへ移しており、6000.7.0b2 同梱の URP Core（旧配置を前提）がコンパイルエラーになる

## EditMode テストの実行

Editor を閉じた状態で、プロジェクトは絶対パスで渡す（`sandbox` だけだと、同じ名前のディレクトリを持つ他の worktree と曖昧になる）:

```bash
unity test <sandbox の絶対パス> --mode EditMode --editor-version 6000.7.0b2 --output <結果の xml の絶対パス>
```

## PlayMode テストの実行

Editor を閉じた状態（batchmode で別プロセスが走る）:

```bash
unity test sandbox --mode PlayMode --editor-version 6000.7.0b2 --filter "Hone.Sandbox.Tests.SandboxSmokeTests" --output sandbox/test-results.xml
```

`sandbox/` で Editor を開いている状態（`unity command` で同じ Editor に投げる）:

```bash
unity command run_tests --mode playmode --filter Hone.Sandbox.Tests.SandboxSmokeTests --filter_type testName
```

どちらも `SandboxSmokeTests.TestRunnerIsAlive`（`Assert.Pass()` 1 件）が通れば基盤は動いている。

`run_tests` が `0/0 passed` を返してテストが走らないときは、`--async_tests true` を付けて実行し、`unity command test_status` が `running` でなくなるまで待って結果を読む（6000.7.0b2、この形でテストが走った）。

`--mode` の値は 2 系統で綴りが違う。`unity test` は `PlayMode` / `EditMode`、`unity command run_tests` は `playmode` / `editor` / `all`（既定 all）。
`--filter` も、`run_tests` は大文字小文字を区別しない部分一致。

## Editor の開き方

1. 先に batchmode でコンパイルが通ることを確かめる。コンパイルエラーがあると GUI の Editor は Safe Mode のダイアログで止まる。

   ```bash
   "C:/Program Files/Unity/Hub/Editor/6000.7.0b2/Editor/Unity.exe" -batchmode -nographics -quit -projectPath <sandbox の絶対パス> -logFile <ログの絶対パス>
   ```

   終了コードが 0 で、ログに `error CS` が無いこと。Unity.exe のパスは Hub 既定のインストール先の例。
2. `unity open <sandbox の絶対パス> --editor-version 6000.7.0b2 --args "-automated"`。
   `-automated` が無いと、初回に URP のマテリアル更新ダイアログが出て、人が閉じるまで `unity command` が 503 を返す。

## Gallery（多言語スクリーンショット）

見た目が変わる PR に添付するスクリーンショットを撮る基盤。`GalleryController.Register(name, factory)` で登録したコンポーネントを、左の ListView（name `components`、登録順）で 1 つ選ぶと、右にそのコンポーネントの列だけが出る。列には、英語（`en`）と、上部の DropdownField（name `language`）で選んだ 1 スクリプトを、短文・長文で並べる。列の中は en が 1 行目、選んだスクリプトが 2 行目。言語を切り替えても選んでいるコンポーネントは変わらず、コンポーネントを切り替えても言語は変わらない。
読み込み時は、リストの先頭（登録順の先頭の `Hone.Button`）が選ばれる。
選択肢は `Gallery/TestStrings.json` の en 以外を書いた順に並び、表示名は各項目の `name`。初期値は en 以外の先頭で、今は `Japanese`（ファイルの並び順を変えると初期値も変わる）。
Unity 標準の `Label` と `Button` は `GalleryController` が自分で登録する（name は `"Label"` `"Button"`）。
`GalleryHoneEntries` が `Hone.Button`（1 つのセルに 4 variant を縦に積む。セルは縦並びで幅が決まるので、長文は省略記号になる）を最初に登録する。リストの初期選択が `Hone.Button` になり、その ring が写る。続けて `Label.hone-text`（`.hone-text` を付けた `Label`）と `Button.hone-focusable`（`.hone-focusable` を付けた `Button`）を登録する。リストで素の `Label` / `Button` と選び比べて、見た目の差を見る。
続けて `Hone.Dialog`（開いた状態。文字列はタイトルと本文に入れ、ボタンは固定の `Cancel` と `OK`）を、最後に `Hone.MessageWindow`（文字列を 2 ページにして 1 ページ目を即時表示し、次のページがある状態の ▼ を写す）を登録する。
列には `gallery-column--<name の "." を "-" にして小文字>` のクラスが付く（例: `gallery-column--hone-dialog`。テストが列の特定に使う。USS では使っていない）。
Dialog と MessageWindow は親の幅に従う部品なので、`Gallery.uss` はセル（`.gallery-dialog`、`.gallery-message-window`）の基準の幅を px で持ち、行の残りまで伸ばしている（`flex-basis: 0` や割合の幅から伸ばすと 1 文字ずつ折り返す）。
`Japanese` では、Screen Space でも Dialog と MessageWindow の en と ja の 2 行が 1 画面に収まった（ほかの言語は確かめていない。6000.7.0b2）。
収まらないときに下まで見るには、`ScrollView`（name `gallery`）の `scrollOffset` を `unity command eval_file` で書き換えながら、`capture_game_view` で複数枚撮る。収めるためにセルの幅（`.gallery-dialog` など）は変えない。
コンポーネントは、ListView の代わりに `GalleryController.SelectComponent` で切り替えて撮る（下の「コンポーネントの切り替え」）。言語も、dropdown の代わりに `GalleryController.SelectScript` で切り替えて撮る（eval から dropdown に届く公開 API が無いため。下の「言語の切り替え」）。
`GalleryFocus` が UI の読み込み後と言語の切り替え後に最初の `.hone-focusable` へフォーカスを当てるので、Play Mode で撮れば ring が写る（言語を切り替えると Dialog が作り直され、各 Dialog が自分の Cancel にフォーカスを移すため、当て直している）。
コンポーネントを選んだときは当て直さない（左のリストを上下で選んでいる途中でフォーカスが右へ飛ぶため）。撮るときは、`SelectComponent` に続けて `GalleryFocus.RefocusLater()` を呼んで当てる。フォーカスを持たないコンポーネント（`Label` など）では、当てる対象が無いだけで何も起きず、エラーも出ない。
Hone.Dialog を選ぶと、Dialog の `Open()` が自分の Cancel にフォーカスを移すので、リストのフォーカスは外れる（Dialog の仕様。直さない）。
背景と文字色は `Gallery.uss` が Hone のトークン（`--hone-color-background` `--hone-color-foreground`）で指定する。

Hone のコンポーネントを載せるときは、`Register` を **sandbox 側のファイル**（例: `Sandbox/Gallery/` 配下）から呼ぶ。`registry/` 配下のコードには書かない（配布物が sandbox の asmdef に依存してしまう）。
呼ぶ時点は `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]`。`GalleryController` は UI が読み込まれた時点の登録内容で左のリストを作り、右の列は、コンポーネントか言語を切り替えるたびに、選んだ name の factory で作り直す。それより後の `Register` はリストに出ず、ログも出ない。
このプロジェクトは Domain Reload を無効にしているので、`Register` した内容は Play をまたいで残る。同じ name の再登録は置き換わる。テストなどで一時的に登録したものは `Unregister` で消す。

撮影手順（Editor は「Editor の開き方」で開いておく）:

```bash
unity command open_scene --path Assets/Hone/Sandbox/Gallery/Gallery.unity --project-path <sandbox の絶対パス>
unity command set_active --target '{"hierarchyPath":"PanelRendererWorldSpace"}' --active false --project-path <sandbox の絶対パス>
unity command editor_play --project-path <sandbox の絶対パス>
# 先頭（Hone.Button）だけ撮るときは、この 1 行を飛ばす。手順は下の「コンポーネントの切り替え」
unity command eval_file --file <select-component.cs の絶対パス> --project-path <sandbox の絶対パス>
# 既定（Japanese）だけ撮るときは、この 1 行を飛ばす。手順は下の「言語の切り替え」
unity command eval_file --file <select-script.cs の絶対パス> --project-path <sandbox の絶対パス>
# 切り替えてから、GalleryFocus の当て直しが終わるまで（DelayMilliseconds の 2 倍）待ってから撮る
MSYS_NO_PATHCONV=1 unity command capture_game_view --source screen --width 1920 --height 1080 --format json --project-path <sandbox の絶対パス> > <json の絶対パス>
python -c "import json,base64; t=open(r'<json の絶対パス>',encoding='utf-8').read(); d=json.loads(t[t.index('{'):]); open(r'<sandbox の絶対パス>/Screenshots/gallery.png','wb').write(base64.b64decode(d['data']['result']['base64']))"
unity command editor_stop --project-path <sandbox の絶対パス>
gh attach --key <ブランチ名> <sandbox の絶対パス>/Screenshots/gallery.png
```

- `screenshot --view game` は使わない。カメラ経由の撮影なので `PanelRenderer`（Screen Space）の UI が写らず、空と地面だけの画像が success で返る（6000.7.0b2）。
- `capture_game_view` は `--save_path` を渡さない。渡すと `Assets/` 配下（`sandbox/Assets/Screenshots/`）に保存され、Texture として import されて `.meta` も増える。返ってきた base64 を自分で書き出す。
- `--width` `--height` を省くと Game view のサイズで撮られる。Play Mode に入ってから撮る。
- `Screenshots/` は gitignore 済み。`gh attach`（`pr-screenshot` スキル）が返す markdown を PR 本文に貼る。

コンポーネントの切り替え（`select-component.cs`。`"Hone.Dialog"` を撮りたい name に替える）:

```csharp
var controllers = UnityEngine.Object.FindObjectsByType<Hone.Sandbox.Gallery.GalleryController>();
if (controllers.Length == 0)
    throw new System.InvalidOperationException("no active GalleryController (is Play Mode on and a panel enabled?)");
foreach (var c in controllers)
{
    c.SelectComponent("Hone.Dialog");
    c.GetComponent<Hone.Sandbox.Gallery.GalleryFocus>().RefocusLater();
}
return controllers.Length;
```

- `SelectComponent` は ListView の `selectedIndex` を変えて、人が選んだときと同じ経路で右を切り替える。すでに選ばれている name を渡すと何も起きない。name は `GalleryController.Names`（左のリストの項目）のどれか。
- `RefocusLater` は `GalleryFocus` が言語の切り替え時に行うのと同じ当て直しを、`DelayMilliseconds` 後に行う。当て直しと確認が終わるまで `DelayMilliseconds` の 2 倍かかる。切り替えと撮影は別のコマンドで行う（同じ eval の中で撮らない）。

言語の切り替え（`select-script.cs`。`"ko"` を `TestStrings.json` の en 以外のキーに替えて撮る）:

```csharp
var controllers = UnityEngine.Object.FindObjectsByType<Hone.Sandbox.Gallery.GalleryController>();
if (controllers.Length == 0)
    throw new System.InvalidOperationException("no active GalleryController (is Play Mode on and a panel enabled?)");
foreach (var c in controllers)
    c.SelectScript("ko");
return controllers.Length;
```

- `SelectScript` は dropdown の `index` を変えて ChangeEvent を出す。人が選んだときと同じ経路で、`GalleryFocus` の当て直しも走る。すでに選ばれている言語を渡すと何も起きない。
- 切り替えの中（Build）で起きた例外は、ChangeEvent の callback の中で出るので eval には返らない。撮る前に Console に Gallery のエラーが出ていないことを確かめる。
- 切り替えてから `GalleryFocus` が当て直すまで、`GalleryFocus` の `DelayMilliseconds` の 2 倍（当て直しと確認）かかる。切り替えと撮影は別のコマンドで行う（同じ eval の中で撮らない）。
- `FindObjectsByType` は有効な GameObject のものだけを返す。片方の panel の GameObject を無効にして撮るときも、そのまま使える。
- eval から `Hone.Sandbox.Gallery` の型は直接参照できる（6000.7.0b2）。`FindObjectsSortMode` を取る版は obsolete なので、引数なしの版を使う。
- eval から dropdown や panel の root に届く公開 API は無い（`PanelRenderer` に root を取る API が無い。6000.7.0b2）。`SelectScript` がその入口。

`Gallery.unity` の `PanelRenderer`（Screen Space）と `PanelRendererWorldSpace`（World Space）は、同じ `Gallery.uxml`、`GalleryController`、`GalleryFocus` を持つ。
World Space 側は `WorldSpacePanelSettings.asset`、`worldSpaceSizeMode = Fixed`、size 1920×1080、位置 (0, 1, 0)（Main Camera の (0, 1, -10) の正面。距離 10 で Game view にほぼ全面で写る）。
両方有効だと重なるので、撮るのは片方だけにする。もう一方を `unity command set_active` で無効化してから Play Mode に入る（上の Screen Space の手順の `set_active` の行がこれ。`GalleryController` に切り替えのフィールドは足していない）。
無効化はシーンに保存しない（保存すると `.unity` に残る）。撮り終えたら、Gallery を保存せずに別のシーンを開いて変更を捨てる（`unity command eval_file` で `EditorSceneManager.OpenScene("Assets/Hone/Sandbox/Sandbox.unity", OpenSceneMode.Single)` を呼ぶと、保存ダイアログは出ずに未保存の変更が捨てられた。6000.7.0b2）。

```bash
unity command open_scene --path Assets/Hone/Sandbox/Gallery/Gallery.unity --project-path <sandbox の絶対パス>
unity command set_active --target '{"hierarchyPath":"PanelRenderer"}' --active false --project-path <sandbox の絶対パス>
unity command editor_play --project-path <sandbox の絶対パス>
# 既定（Japanese）だけ撮るときは、この 1 行を飛ばす（select-script.cs は上の「言語の切り替え」）
unity command eval_file --file <select-script.cs の絶対パス> --project-path <sandbox の絶対パス>
MSYS_NO_PATHCONV=1 unity command screenshot --view game --output <sandbox の絶対パス>/Screenshots/gallery-worldspace.png --width 1920 --height 1080 --project-path <sandbox の絶対パス>
unity command editor_stop --project-path <sandbox の絶対パス>
```

- World Space は `screenshot --view game` で写る（カメラ経由で描画されるため）。
- `set_active` の `--target` に `hierarchyPath` で指定した名前は完全一致で、`PanelRenderer` は `PanelRendererWorldSpace` を巻き込まない（6000.7.0b2 で、`PanelRenderer` だけを無効化して World Space 側が写ることを確認）。
- コンポーネントと言語の切り替えは Screen Space と同じ `SelectComponent` と `SelectScript`。World Space の panel は論理サイズが 1920×1080 で、`Japanese` では Dialog も MessageWindow も en と ja の 2 行が 1 画面に収まった（ほかの言語は確かめていない。6000.7.0b2）。収まらないときは、Screen Space と同じく `ScrollView`（name `gallery`）の `scrollOffset` を書き換えながら複数枚撮る。
- 同じ Gallery でも、World Space は論理サイズが 1920×1080 の panel なので、Screen Space と列の幅が違い、省略記号になる位置も違う（6000.7.0b2。原因は調べていない）。

撮った画像で、文字列が読めるか、豆腐（□）になっていないか、行や列がはみ出していないかを人が見る。画像の自動比較はしない。

フォントは同梱しない。既定フォントと fallback で、ar、th などがどう描画されるかを見るのも、この画像の目的のひとつ。

## FontVar の Player 検証

`FontVar.unity` は Build Settings に入れていないので、ビルド時にシーンを指定する。`unity command build` は 30 秒でコマンド自体が timeout するが、ビルドは続くので `build_status` で完了を待つ。

```bash
unity command build --project-path <sandbox の絶対パス> --target StandaloneWindows64 --outputPath <sandbox の絶対パス>/Build/FontVar/FontVar.exe --scenes '["Assets/Hone/Sandbox/Experiments/FontVar/FontVar.unity"]' --confirm true
unity command build_status --project-path <sandbox の絶対パス>
<sandbox の絶対パス>/Build/FontVar/FontVar.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -fontvar-shot <png の絶対パス> -logFile <ログの絶対パス>
```

判定はログの `[FontVarProbe]` 行（Label ごとの `fontAsset`）で行う。スクリーンショットは補助。Probe は失敗時に `LogError` を出し、終了コード 1 で終了する。

## TokensExp の検証

`TokensExp.unity` は `PanelRenderer` を 2 枚置く。`hone` は `Sandbox/PanelSettings.asset`（`HoneTheme.tss`）、`nofont` は `TokensExpNoFont.tss`（`--hone-font-body` を定義しない theme。`:root` に `url("/Assets/…")` と `resource("…")` の変数を置く）。
`TokensExpProbe` が Label の `resolvedStyle.unityFontDefinition` と、`.hone-focusable` の Button に `Focus()` した前後の border をログ（`[TokensExpProbe]`）に出し、最後に `RESULT OK|FAIL` を出す。
`-tokensexp-shot <png>` を渡した Player は、スクリーンショットを保存して終了する（測定の前提が崩れたときは終了コード 1）。Editor の Play Mode では終了しない。

`TokensExp.unity` の依存の中では、`Sandbox/Fonts/RobotoMono.asset` を直接参照するのは `HoneTheme.tss` の `:root` だけにしてある
（プロジェクト全体では FontVar の USS からも参照されるが、このシーンの依存には入らない。`nofont` の 2 つの変数は、同名の Resources 側の複製 `FontVar/Resources/Fonts/RobotoMono.asset` を指す。
Probe は `Resources.Load` で取った複製との同一性で区別する）。Player の検証はこの状態が前提なので、シーンや theme を変えたらビルド前に次で確かめる（`deps.cs` は任意の場所に置く）。

```csharp
// deps.cs。unity command eval_file --file <deps.cs の絶対パス> --project-path <sandbox の絶対パス>
var font = "Assets/Hone/Sandbox/Fonts/RobotoMono.asset";
var referrers = new System.Collections.Generic.List<string>();
foreach (var dep in UnityEditor.AssetDatabase.GetDependencies("Assets/Hone/Sandbox/Experiments/TokensExp/TokensExp.unity", true))
    if (dep != font && System.Array.IndexOf(UnityEditor.AssetDatabase.GetDependencies(dep, false), font) >= 0)
        referrers.Add(dep);
return string.Join(",", referrers); // Assets/Hone/HoneTheme.tss だけが返れば前提どおり
```

```bash
unity command build --project-path <sandbox の絶対パス> --target StandaloneWindows64 --outputPath <sandbox の絶対パス>/Build/TokensExp/TokensExp.exe --scenes '["Assets/Hone/Sandbox/Experiments/TokensExp/TokensExp.unity"]' --confirm true
unity command build_status --project-path <sandbox の絶対パス>
timeout 120 <sandbox の絶対パス>/Build/TokensExp/TokensExp.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -tokensexp-shot <png の絶対パス> -logFile <ログの絶対パス>
```

結果（Unity 6000.7.0b2、Windows）。「一回限り」と書いた行以外は Probe が毎回判定している値で、Editor の Play Mode と Player で同じだった。「一回限り」の行は、実験用に一時的に書き換えて Editor で測ったもので、今のファイルでは再現しない:

| 確認 | 結果 |
|---|---|
| `HoneTheme.tss` の `:root` の `--hone-font-body: url("project://…")` を `.hone-text` が使う | 通る。`hone.body` が `Sandbox/Fonts` の FontAsset になる。Player でも、この `:root` の変数が唯一の参照元の状態で通る |
| `:root` の変数に `url("/Assets/…")`、`resource("…")` を書く | どちらも Editor と Player で通る（`nofont.path` `nofont.res`）。ただし指す先が Resources 側の複製で、Player ではビルドに必ず入るため、Player で「参照だけでビルドに含まれるか」は見ていない |
| `--hone-font-body` が未定義のとき `.hone-text` | 祖先にフォント指定が無ければ `fontAsset=null`（Unity の既定）。祖先にフォント指定があれば、その値を継承する（`nofont.inherit`。null には戻らない）。警告は出ない |
| `--hone-font-body` が定義済みで、祖先に別のフォント指定があるとき | `.hone-text` の指定が勝つ（`hone.inherit`） |
| （一回限り、Editor のみ）`.hone-focusable:focus`（クラス 1 つ + `:focus`）を `Core.uss` に書く | 既定テーマの Button の `:focus` の枠の色 `rgb(0,106,166)` が勝つ。幅（`--hone-ring-width`）だけ効く。`HoneTheme.tss` 自身に書いた同じ形の規則も負ける。`border-color` を 4 辺の個別指定にしても負ける。UXML の `<Style>` に書いた同じ形の規則は勝つ |
| `.hone-focusable.hone-focusable:focus`（クラスを 2 回）を `Core.uss` に書く | `--hone-color-ring` の色 `rgb(24,24,27)` と幅 2 が効く |
| `HoneTheme.tss` の `@import url("Tokens.uss")` `@import url("Core/Runtime/Core.uss")`（`HoneTheme.tss` 起点の相対パス） | 通る（`.hone-text` と `.hone-focusable` が効く） |

未確認: 2 枚目の `PanelRenderer`（`nofont`）の Button は `Focus()` してもフォーカスを取れなかった（原因は調べていない）。ring の測定は `hone` の panel だけで行っている。

## FocusTrap の Player 検証

`FocusTrap.unity`（EventSystem なし）と `FocusTrapEventSystem.unity`（EventSystem + `InputSystemUIInputModule` あり）は Build Settings に入れていないので、ビルド時にシーンを指定する。ビルドの待ち方は FontVar と同じ。
`FocusTrapProbe` は Input System の合成デバイス（Gamepad、Keyboard）へ入力を積み、UI map の既定バインディングを通して全ステップを自動で回し、終了する。
`Nav:*` のステップだけは `NavigationMoveEvent` を `SendEvent` で直接送り、入力層を通らない。

```bash
unity command build --project-path <sandbox の絶対パス> --target StandaloneWindows64 --outputPath <sandbox の絶対パス>/Build/FocusTrap/FocusTrap.exe --scenes '["Assets/Hone/Sandbox/Experiments/FocusTrap/FocusTrap.unity"]' --confirm true
unity command build_status --project-path <sandbox の絶対パス>
<sandbox の絶対パス>/Build/FocusTrap/FocusTrap.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile <ログの絶対パス>
```

EventSystem あり側は、シーンのパス、`--outputPath`、exe 名の 3 箇所を `FocusTrapEventSystem` に読み替え、ログも別のパスにする。
終了コードを見るときは bash から起動するか、PowerShell なら `Start-Process -Wait -PassThru` で待つ（GUI の exe は既定で待たない）。

Probe は trap の成否を判定しない。測定の前提（開始フォーカス、外へ出る対照ステップで入力と描画検出が届いたか、シーンと EventSystem の有無の一致、Error / Exception のログ）が崩れたときだけ `[FocusTrapProbe] FAIL` を出し、終了コード 1 で終了する。
`DONE steps=N failed=0` なら前提は崩れていない。結果はログの `[FocusTrapProbe] RESULT` 行（1 ステップ 1 行）を読む。

| 項目 | 意味 |
|---|---|
| `eventSystem` | scene に `EventSystem` があるか |
| `case` | Issue #12 の表のケース番号。`3-fresh` は起動直後（Probe が一度も `Focus()` を呼んでいない）のケース 3。ケース 4 は専用ステップを持たず、EventSystem あり側で同じステップを回す |
| `mode` | trap の方式。値の意味は `FocusTrapProbe.cs` の `enum Mode` のコメント |
| `start` | 押す前に `Focus()` した要素の指定。`(none)` は `Focus()` しない |
| `input` | 押した入力。`Up` `Down` `Left` `Right` は Gamepad の D-pad、`Tab` `ShiftTab` は Keyboard の state + text event、`TabStateOnly` は state のみ、`Nav:*` は `SendEvent` |
| `startFocused` | 押す直前に実際にフォーカスされていた要素 |
| `final` | +8 の frame 終端のフォーカス |
| `inTrap` | `final` の要素がコンテナ `trap` の中にあるか |
| `firstEvent` | 入力が UI Toolkit のイベント（`NavigationMoveEvent` / `KeyDownEvent`）として最初に届いた frame。`n/a` は届かなかった |
| `settled` | frame 終端の標本で `final` と同じ値が最後まで続き始めた最初の frame。`unchanged` は一度も `startFocused` から動かなかった |
| `outsideSampled` | どれかの標本（`upd` か `eof`）で外側の要素にフォーカスがあったか |
| `outsidePainted` | 外側の要素がフォーカス色で描画されたことが 1 回でもあったか（`generateVisualContent` での代理指標） |
| `samples` | `+N[upd=…,eof=…]`。`upd` は Update 後のコルーチン再開時点（UI Toolkit の処理との前後は未確認）、`eof` は `WaitForEndOfFrame` 後。+0 は押した frame で、`upd` は `-` |
| `notes` | `+N` 付きのイベント・handler・描画の記録 |

frame は押した frame を +0 と数える。D-pad と Tab は入力が次の frame で UI Toolkit に届くので、最小は +1。`Nav:*` は +0 の中で処理される。

## ThemeExp の Player 検証

`ThemeExp.unity`（`PanelRenderer` 3 枚: 絶対パス import の theme、相対パス import の theme、`unity-theme://default` のみの theme）は Build Settings に入れていないので、ビルド時にシーンを指定する。ビルドの待ち方は FontVar と同じ。
`ThemeExpProbe` が case 1〜4 を自動で回し、ログの `[ThemeExpProbe]` 行に値を出す。最後に `RESULT OK` か `RESULT FAIL` の行を出すので、この行が無いログは途中で止まったものとして扱う。
`-themeexp-out <dir>` を渡すと、スクリーンショット（`case2-unfocused.png` `case2-focused.png` `case4-full.png`）を `<dir>` に保存して終了する。測定の前提が崩れたとき（`RESULT FAIL`、120 秒の超過を含む）は終了コード 1。Editor の Play Mode では `Application.Quit` が効かないので、フラグを付けても終了しない。
フラグを付けないとスクリーンショットは保存されず、自動で終了もしない。

```bash
unity command build --project-path <sandbox の絶対パス> --target StandaloneWindows64 --outputPath <sandbox の絶対パス>/Build/ThemeExp/ThemeExp.exe --scenes '["Assets/Hone/Sandbox/Experiments/ThemeExp/ThemeExp.unity"]' --confirm true
unity command build_status --project-path <sandbox の絶対パス>
timeout 120 <sandbox の絶対パス>/Build/ThemeExp/ThemeExp.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -themeexp-out <dir の絶対パス> -logFile <ログの絶対パス>
```

`timeout 120` は、Probe 自身の 120 秒のウォッチドッグが効かない固まり方（主スレッドが戻らない）に備えた外側の保険。ログの `CASE4 add` の `+1` は、ラベルを追加した frame の処理時間（`+0` は無い）。

- `ThemeExpMissing.tss` はどのシーンからも使わない。存在しないパスを `@import` したときの症状を見るためのもので、絶対パスと相対パスの 2 行はどちらも同じ `.../ThemeExp/Tokens/Missing.uss` に解決される。`.tss` を再インポートすると Editor.log に `warning: Invalid asset path: '<解決後のパス>'` が 2 行出る。Console には出ない（6000.7.0b2）。`.tss` は Assets 内にあるので、プロジェクトの再インポートのたびに Editor.log に出る。
- Player の Profiler マーカーは、開発ビルドでない Player では取れなかった（6000.7.0b2、Windows。`ProfilerRecorderHandle.GetAvailable` が 82 件で、Probe が対象にするマーカー（カテゴリが `UI` で始まり、名前が `Layout|Font|Text|Glyph|UIR|UIElements|Atlas` に合うもの）が 0 件）。ログでは `CASE4 profiler markers matched=0`。開発ビルドで取れるかは未確認。マーカーが要るときは Editor の Play Mode で回す。ただし Editor の値には、Inspector や Game view など Editor 自身の UI Toolkit パネルの分が同じ frame に合算される。Probe が拾うのは起動時点で登録済みのマーカーだけで、初回ロードで初めて通るコードのマーカーは拾えない可能性がある。
- Editor の Play Mode では、動的に作られた FontAsset と atlas が前回の Play Mode から残ることがあり、case 4 の「初回」の時間にならない。時間は Player の値を読む。
- `CASE3` は `TextSettings` の内部 API を reflection で読む（実験用）。Player では runtime 生成の FontAsset の `name` が空になる（6000.7.0b2）。Editor では `Arial - Regular SDF`。fallback は `faceInfo.familyName` で出している。
- `CASE4` は、日本語が豆腐になっていないかをコードでは判定しない。`case4-full.png` を人が見る。

## WorldSpace の Player 検証

`WorldSpace.unity` は Build Settings に入れていないので、ビルド時にシーンを指定する。ビルドの待ち方は FontVar と同じ。
シーンは Main Camera（(0, 1, -10)）、EventSystem + `InputSystemUIInputModule`、`PanelInputConfiguration`（`processWorldSpaceInput = true`、event camera は Main Camera）、World Space の `PanelRenderer` 2 枚。
どちらも UXML は `WorldSpace.uxml` で、`Hone.Button`（name `target`）を panel の中央に 1 つと、閉じた `Hone.Dialog`（name `dialog`。`Dialog.uxml` の使用例と同じ中身）を root 直下の最後の子に置く。

| panel | GameObject | PanelSettings | 大きさ（`worldSpaceSizeMode = Fixed`） | 位置 |
|---|---|---|---|---|
| 1 枚目 | `PanelRenderer`（`WorldSpaceProbe` が付く） | `WorldSpacePanelSettings.asset` | 1920×1080 | (0, 1, 0) |
| 2 枚目 | `PanelRenderer2` | `WorldSpacePanelSettings2.asset` | 480×300（1 つのダイアログ程度。`--hone-size-dialog` の 512px より狭い） | (5, 1, -2)。1 枚目の手前の右側で、カメラに収まり、1 枚目の Button の中心と重ならない |

`WorldSpaceProbe` は Input System の合成 `Mouse` を作り、ポインタを要素の位置へ動かして press / release を積む。
要素の screen 座標は、要素の `worldBound` の中心を、panel の root の `worldBound` の中心を原点にした値とみなし、`PanelRenderer` の `transform`（panel の中央が位置）で world に直して `Camera.WorldToScreenPoint` に渡して出す。
前提の確認で、各 panel の中央にある Button の中心がこの変換で `transform.position` の screen 座標（2px 以内）に一致することを確かめる。`Screen.width` などは見ない。

ステップ（1 ステップが `RESULT` 行を 1 行出す）:

| ステップ | 対象 | 操作 | 判定 |
|---|---|---|---|
| `hover` | 1 枚目の Button | ポインタを中心へ動かす | `resolvedStyle.opacity` が 1 から 0.9 になる |
| `active` | 同上 | press したまま | `opacity` が 0.8 |
| `focus` | 同上 | release の後 | `focusedElement` が Button で、`borderTopWidth` が押す前より大きい |
| `click` | 同上 | press / release | `clicked` が 1 回 |
| `open` | 1 枚目の Dialog | `Open()` | content が panel の root の中に収まり、幅が `--hone-size-dialog`（max-width）で決まる |
| `overlay` | 同上 | content の外側（panel の左上の角から 12px）を press | `closed` が 1 回発火し、`isOpen` が false |
| `footer` | 同上 | `Open()` して OK の中心を press / release | OK の `clicked` が 1 回 |
| `open-narrow` | 2 枚目の Dialog | `Open()` | content が panel の中に収まり、幅が panel の 90% で決まる |
| `cross-click` | 1 枚目の Button と 2 枚目の Dialog の OK | 続けてクリック | どちらの `clicked` も 1 回 |
| `cross-focus` | 両 panel | 1 枚目の Button に `Focus()` してから、2 枚目の Dialog を `Open()` → `Close()` | 判定しない（前提の、1 枚目の Button がフォーカスを得たことだけ判定）。Open 前 / Open 後 / Close 後の両 panel の `focusedElement` を記録する |

```bash
unity command build --project-path <sandbox の絶対パス> --target StandaloneWindows64 --outputPath <sandbox の絶対パス>/Build/WorldSpace/WorldSpace.exe --scenes '["Assets/Hone/Sandbox/Experiments/WorldSpace/WorldSpace.unity"]' --confirm true
unity command build_status --project-path <sandbox の絶対パス>
timeout 120 <sandbox の絶対パス>/Build/WorldSpace/WorldSpace.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile <ログの絶対パス>
```

ログの `[WorldSpaceProbe]` 行を読む。`RESULT step=<名前> … pass=true|false` の行が全ステップ分と、最後に `DONE steps=<件数> failed=0` が出て終了コード 0 なら、全ステップが期待どおり。
Probe は `HEADER`（renderMode、PanelSettings、size mode、layout の大きさ、root と Button の `worldBound`、EventSystem、`PanelInputConfiguration` の有無、Main Camera が event camera か、Button の型）と `POINTER`（要素の `worldBound` と screen 座標）も出す。
失敗はすべて `[WorldSpaceProbe] FAIL <理由>` の行で出し、最後に `[WorldSpaceProbe] FAIL total=<件数> first=<最初の理由>` を出して終了コード 1 で終了する（`DONE` は出さない）。失敗に数えるのは次のとおり:

- panel が 300 frame 以内に準備できない、または 60 秒以内に終わらない
- 測定の前提（2 枚目の panel がある、Button が `Hone.Button` で `worldBound` を持つ、2 枚の PanelSettings が World Space で別の asset、`worldSpaceSizeMode = Fixed`、Dialog が閉じている、EventSystem + `InputSystemUIInputModule`、`processWorldSpaceInput = true` の `PanelInputConfiguration`、Main Camera が event camera、Button の中心の変換が合う）が崩れている。このときは何も押さず、`RESULT` 行も出さない
- どれかのステップの期待値が外れた（`pass=false`）。押した点がカメラの外や後ろにある場合も含む
- Probe 自身以外の Error / Exception / Assert のログ（Probe の `Awake` から終了まで）

`DONE` も `FAIL total=` も無いログは、Probe が動かなかった（シーン違い、コンポーネント欠落）か途中で止まったものとして扱う。外側の `timeout 120` はそのときの保険。
合成 Mouse のほかに実マウスがつながっていると、実マウスの動きでポインタがずれてクリックが外れる可能性がある（未確認。起きても `clicked` が 0 回で FAIL になる側に倒れる）。
Editor の Play Mode では `Application.Quit` が効かないので終了しないが、ログは同じものが出る。

結果（Unity 6000.7.0b2、Windows の Player。3 回回して同じ。`steps=10 failed=0`）:

- World Space の panel では `worldBound` が world 単位になる（pixels per unit 100 の既定のままで、`Hone.Button` は幅 1.27、高さ 0.37）。ただしシーンの world 座標ではなく、panel の位置を原点にした値で、panel（位置 (0, 1, 0)）の中央にある Button の中心はほぼ (0, 0) だった。`worldBound` を `Camera.WorldToScreenPoint` に直接渡しても screen 座標にならない。
- `worldBound` の y は上向き。layout で下にある footer の OK の `worldBound.y` は、root の中心（root の `worldBound` は y -5.4 から 5.4）より小さい負の値だった（-0.66）。上のとおり、root の中心を原点にした値を、y を反転せずに `transform` を通して world に直すと、`Camera.WorldToScreenPoint` の screen 座標でクリックが届く。y を反転すると、footer の OK が中心より上に写り、OK の `clicked` が届かない。
- 1 枚目の Button は、既定の variant で hover の `opacity` が 0.9、active が 0.8、クリックの後に `focusedElement` が Button になり `borderTopWidth` が 1 から 2 になり、`clicked` が 1 回届く。
- 1 枚目（1920×1080）の Dialog は、content が 512×182px で panel の中に収まり、幅は `--hone-size-dialog`（90% は 1728px）で決まる。overlay の押下で閉じ、閉じた後のフォーカスは開く前の Button に戻る。footer の OK の `clicked` が届く。
- 2 枚目（480×300）の Dialog は、content が 432×182px（panel の幅の 90%）で、panel の中に収まる。
- `cross-click`: 2 枚目の Dialog を開いたまま、1 枚目の Button と 2 枚目の OK の `clicked` がどちらも届く。
- `cross-focus`: 1 枚目の Button にフォーカスがある状態から 2 枚目の Dialog を `Open()` すると、2 枚目の Cancel にフォーカスが移り、1 枚目の Button はフォーカスを失った（1 枚目の `focusedElement` が null）。`Close()` の後は両 panel とも null で、1 枚目の Button には戻らない。

## RoomExp の検証

モデルルームの先頭のルームと文字送りの部品が前提にする、UI Toolkit の挙動の検証。Unity 6000.7.0b2（`ProjectSettings/ProjectVersion.txt`）、Windows の Editor の Play Mode で確かめた。

`RoomExp.unity` の構成:

| GameObject | 中身 |
|---|---|
| `Main Camera` | 位置 (0, 1, -10)、縦の FOV 60 |
| `PanelRenderer` | Screen Space。`RoomExpPanelSettings.asset`（`Sandbox/PanelSettings.asset` の複製で、Theme Style Sheet を `RoomExp.tss` に、scale mode を ConstantPixelSize にしたもの。scale は元の 1 のまま）、`RoomExp.uxml`。`RoomExpProbe` が付く |
| `PanelRendererWorldSpace` | World Space。`RoomExpWorldSpacePanelSettings.asset`（`Sandbox/WorldSpacePanelSettings.asset` の複製で、Theme Style Sheet だけ `RoomExp.tss` にしたもの）、`RoomExpWorld.uxml`（(b) と (c) だけ）。760×470、位置 (5.4, -1.65, -0.647)、回転なし |

theme はどちらも `RoomExp.tss`。`HoneTheme.tss` と同じ @import（Dialog を除く）と `:root` の `--hone-font-body`（RobotoMono）に、`RoomExp.uss` の @import を足したもの。
World Space の panel は、カメラから 9.353 の距離に置いてある。描画解像度の高さが 1080px のとき、panel の 1px（pixels per unit 100 なので 0.01 world 単位）が画面の 1px に写る距離で、
1080 × 0.01 / (2 × tan(FOV / 2)) から出した。FOV、描画解像度、pixels per unit のどれかを変えたら距離も出し直す。

```bash
unity command open_scene --path Assets/Hone/Sandbox/Experiments/RoomExp/RoomExp.unity --project-path <sandbox の絶対パス>
unity command editor_play --project-path <sandbox の絶対パス>
unity command console --project-path <sandbox の絶対パス>
unity command editor_stop --project-path <sandbox の絶対パス>
```

ログの `[RoomExpProbe]` 行を読む。`RESULT item=<項目> ok=<true|false> …` が 19 行と、最後に `DONE results=19 expected=19 mismatches=<件数> failures=<件数>` が出る。
`ok` は Issue の仮説どおりだったかで、`false` は結果であって Probe の失敗ではない（今は `e-textelement` の 1 件が `false`。下の表）。
測定の前提が崩れたとき（panel が準備できない、要素が無い、glyph が取れない、対照が期待どおりでない、Probe 以外の Error のログ、60 秒の超過）は `[RoomExpProbe] FAIL` を出して `failures` に数え、その項目の `RESULT` は出さない。
`DONE` の時点で `RESULT` が出ていない項目も `FAIL` にする。`DONE` は、途中で例外が出て止まっても 60 秒の時点で出る。
(c) は判定しない。`HEADER` 行に各 Label の FontAsset の設定を出すので、スクリーンショットを人が見る。

(c) を撮るときは、先に Game view の描画解像度を 1920×1080 に固定する（`unity command eval_file` で `UnityEditor.PlayModeWindow.SetCustomRenderingResolution(1920, 1080, "RoomExp 1920x1080");`。
6000.7.0b2 では、この呼び出しで解像度の項目が追加され、Game view がその解像度で描画するようになった）。理由は「既知の事項」の `capture_game_view` の項目。
撮影は `capture_game_view --source screen --width 1920 --height 1080`（Gallery の撮影手順と同じ。World Space の panel も写る）。撮った後は Dynamic の FontAsset のデータを消す（「既知の事項」）。

表の値の出どころは 3 つある。「Probe」と書いたものは `RESULT` 行の値で、Play Mode に入れば毎回出る。それ以外は、検証のときに一度だけ手で測ったもので、リポジトリから自動では再現しない。

- (b) の `@keyframes` と `animation-name: <名前>` の扱い: その形の USS を import して、Console の警告と、できた StyleSheet の規則の数を見た。
- (b) の `RoomExpBlink.asset` の作り方: Editor の内部の型を reflection で呼んだ。`Unity.UIToolkit.Editor.Utilities.UIAnimationClipFactory.CreateNewAsset(<パス>)` でアセットを作り、
  `UnityEditor.UIElements.UIAnimationBinderEditorBindings.GetAllAnimatableProperties` で binding 名を調べ（`UIAnimationBinder` の `exposeRootElement` を true にすると要素自身の `Opacity` が出る）、
  `AnimationUtility.SetEditorCurve` で中の `AnimationClip` に curve を入れた。
- (c) の中間調の画素の割合: 1920×1080 のスクリーンショットで、各 Label の行の範囲のうち、明るさ（0〜255）が背景（24）+ 20 より大きい画素を文字の画素とし、そのうち 230 未満のものの割合を出した。小さいほど縁がにじまない。
- (e) の ar と th の画素比較: 同じスクリーンショットで、途中まで表示した Label と全部表示した Label を、Label の背景色から求めた枠で切り出し、見えている範囲の 1 行目の画素の差を数えた。

結果:

| # | 確認 | 結果 | 採る方式 |
|---|---|---|---|
| (a) | ▶ のカーソルを `:focus` の `background-image` で描けるか | 通る（Probe）。`.room-exp .hone-button:focus`（クラス 2 つ + `:focus`）に `background-image: url("RoomExpCursor.png")`、`background-position-x: left 6px`、`background-position-y: center`、`background-repeat: no-repeat`、`background-size: 8px 8px` を書くと、`resolvedStyle.backgroundImage` がフォーカス前の none から `RoomExpCursor` になる。<br>`layout.width` はフォーカス前後とも 76 で変わらない（親に引き伸ばされない配置で測った）。ただしこれは `padding-left` と `border-width` を状態によらず同じ値にした `.room-exp .hone-button` の規則だけでなく、(d) の `:focus` の規則が Core.uss の ring（`border-width` を変える）を抑えているから。`.room-exp .hone-button` の `border-width: 0` だけでは、フォーカスで ring の幅が付き、幅が変わる。<br>確かめていないこと: クラス 1 つ + `:focus` の規則で書いた場合、既定テーマの Button の `:focus` が触るプロパティ（枠の色）と同じプロパティを書いた場合 | カーソルは `:focus` の `background-image` で描く。`padding-left` は状態によらず同じ値を置き、`border-width` は (d) の形の `:focus` の規則でも固定する（ring とカーソルを両方出すと、ring の分だけ幅が変わる） |
| (b) | ループするアニメーションの手段 | `@keyframes` は受け付けない（手で import して確認）。規則ごと警告なしで捨てられる。`animation-name: <名前>` は `warning: Expected ([ <resource> \| <url> \| none ]#) but found '<名前>'` になる。6000.7 の `animation-name` は、`UIAnimationClip` のアセット（中身は `AnimationClip`）を `url()` で指す形。<br>`UIAnimationClip`（`RoomExpBlink.asset`。opacity の curve、0.5 秒、ループ）を `animation-name: url("RoomExpBlink.asset")` と `animation-iteration-count: infinite` で指すと、Screen Space と World Space の両方で opacity が 0〜1 で動く（Probe）。panel から外して戻すと再開する。ただし作成は UI Toolkit の Animation の Editor が前提で（今回は上のとおり Editor の内部の型を reflection で呼んで作った）、curve の binding 名（要素自身は `Opacity`。子要素は `GetAllAnimatableProperties` が `#<name>/Opacity` を返した）は文書化されていない。<br>`schedule.Execute(...).Every(250)` で `is-dim` クラスを付け外しし、USS の `transition` で動かす形は、両方の panel で動く（Probe）。panel から外している 1 秒の間は tick が 0 回で、戻すと再開する。<br>`experimental.animation`（250ms の 1→0 と 0→1 を `OnCompleted` でつなぐ）も同じく、両方の panel で動き、外すと止まり、戻すと再開する（Probe）。1.5 秒の測定で callback の回数と frame 数が同じ（334 回、334 frame）で、毎 frame 呼ばれていた | `schedule.Execute(...).Every(ms)` でクラスを付け外しし、`transition` で動かす。公開 API だけで済み、何をどう動かすかは状態クラスと `transition` として USS に残るので、利用者が書き換えられる（部品は状態クラスを出すだけで演出を持たない、というモデルルームの方針に合う）。`experimental.animation` は動かす値を C# に書くことになる。USS の `animation-name` は、`UIAnimationClip` の作り方と binding 名が文書化されたら比べ直す |
| (c) | ピクセルフォントが Screen Space と World Space でにじまずに描けるか | DotGothic16 で、描画方式ごとに `font-size` 16px と 32px を並べ、ドットの縁の中間調の画素の割合（上の手で測った値。小さいほどにじまない）を測った。Screen Space（ConstantPixelSize、scale 1）と、上の 1:1 の配置の World Space で同じ値だった。<br>SDFAA、sampling 90（Hone > Sync と同じ値）: 16px で 0.85、32px で 0.46。縁がにじむ。<br>SDFAA_HINTED、sampling 16: 16px で 0.99、32px で 0.92。SDFAA、sampling 90 よりにじむ。<br>RASTER_HINTED、sampling 16、atlas の Filter Mode Point を Advanced Text Generator で描く: 字形が壊れる（字の上に別の字の断片が重なる。atlas を Bilinear にしても同じ）。<br>同じ FontAsset を `-unity-text-generator: standard` で描く: 16px も 32px も 0.00。ドットがそのまま出る。<br>atlas の Point は、Dynamic のデータを消して atlas が作り直された後も残った（`HEADER` 行） | DotGothic16.asset は RASTER_HINTED、sampling 16（DotGothic16 のドットの格子）、padding 1、atlas の Filter Mode は Point、multi atlas は無効。使う範囲に `-unity-text-generator: standard` を置き、`font-size` は 16 の整数倍にする。Standard の生成器では `Glyph.textRange` と `parsedText` が使えない（(e)）。<br>確かめていないこと: Screen Space の scale mode が ScaleWithScreenSize や ConstantPhysicalSize のときと、DPI などで panel の 1px が画面の整数 px にならないとき、World Space の panel を拡大・縮小・傾けたとき、Standard の生成器で ar や th を描いたときのシェーピング、atlas（1024×1024）に入りきらない数の glyph を使ったとき（multi atlas が無効なので） |
| (d) | 親クラスで範囲を絞った `--hone-font-body` の上書きと、範囲を絞った規則の勝ち負け | 通る（Probe）。`.room-exp { --hone-font-body: url("project://…DotGothic16.asset…"); }` で、`.room-exp` の中の `.hone-text` は DotGothic16、外は `:root` の RobotoMono になる。同じ規則に書いた `-unity-text-generator: standard` も中の Label に継承される（中は Standard、外は Advanced）。<br>`.room-exp .hone-focusable.hone-focusable:focus { border-width: 0 }`（クラス 4 つ分の詳細度）は Core.uss の ring に勝つ（フォーカス後の `borderTopWidth` が 0）。対照の、`.room-exp` の外で同じく `border-width: 0` を置いた Button は、フォーカスで ring の 2 になる（Probe。対照が 2 にならなければ Probe は FAIL にする）。この形が勝つのに必要な最小の形かどうかは確かめていない。<br>この USS は theme（`RoomExp.tss`）から @import して読み込めた | ルームの範囲は `.room-<genre>` の 1 クラスで絞り、フォントは `--hone-font-body` の上書き、focus ring の上書きは `.room-<genre> .hone-focusable.hone-focusable:focus` の形で書く。ルームの USS は theme から @import する |
| (e) | 文字送りで、行の折り返しと高さが動かない方式 | 候補 1（表示済みの部分はそのまま、未表示の部分を `<alpha=#00>` で透明にする）で通る（Probe）。Gallery の `TestStrings.json` の長文を幅 180px、16px の Label に入れ、表示した文字数 0 / 半分 / 全部（ar はつながる文字の境界で切ったものも足して 4 つ）で比べた。2 つの条件で測った。<br>**Advanced Text Generator、フォント指定なし**（Unity の既定フォントと PanelTextSettings の fallback。`.room-exp` の外）: `layout.height` は ja 73、en 45、ar 45、th 45 で、言語ごとにすべて同じ。各行の先頭の文字も同じ。未表示の文字の glyph だけが頂点の alpha 0 で、`parsedText` は本文と同じ。glyph の行と位置は `TextElement.PostProcessTextVertices` の `Glyph.line` と `Glyph.textRange` で取れ、`textRange` は `text` ではなく `parsedText`（タグを除いた文字列）の位置を指す。<br>**Standard の生成器、DotGothic16**（`.room-exp` の中。(c)/(d) の推奨の条件。ja と en だけ）: `layout.height` は ja 78、en 55 で、それぞれ同じ。`Glyph.textRange` と `parsedText` は `NotImplementedException`（Advanced の生成器でしか実装されていない）になるので、行の折り返しは行ごとの glyph の数（ja 11 / 11 / 5、en 18 / 16）で比べ、同じだった。alpha は glyph の並び順で、表示済みの glyph が先頭に連続し、その後ろがすべて 0 だった。Standard では空白が glyph にならない（en の glyph は 34。Advanced は 39）。<br>ar の途中まで表示した字形は、全部表示したときと同じ（手で測った値）。半分の位置はこの文字列ではちょうどアレフ（左につながらない文字）の直後で、見えている範囲の画素が全部表示と完全に一致した。左につながる文字の間で切っても、見えている側の文字はつながる形のまま描かれ、違いは境界の 5px だけだった（シェーピングは未表示の部分を含む文字列全体で行われている）。th も半分の表示の 1 行目が全部表示と画素単位で一致した。<br>本文の `<` は `<noparse>` で囲めばタグとして解釈されない（`HP<10 <b>bold</b>` の `parsedText` が本文と同じ）。ただし本文に `</noparse>` そのものがあると、大文字小文字を問わず（`</NOPARSE>` でも）そこで閉じて以降のタグが解釈される（`a</noparse><b>b</b>` が `ab` になる）。本文の `</noparse>` を大文字小文字を問わずに探し、`<` + `</noparse>` + 元の文字列の 2 文字目以降 + `<noparse>` に置き換えると、文字のまま出る（Advanced で確認）。<br>`StringInfo` の text element の数（Mono 6.13）: 分解形の「が」、e + 結合アクセント、サロゲートペアの絵文字、タイ語の声調記号付きの文字、アラビア語のファトハ付きの文字は 1。ZWJ でつないだ家族の絵文字は 5、国旗（regional indicator 2 つ）は 2、タイ語の SARA AM（ำ）付きの文字は 2 になる | 候補 1 で組む。表示済みと未表示の部分をそれぞれ `<noparse>` で囲み、本文の `</noparse>` は上の形に置き換え、その間に `<alpha=#00>` を置く。1 文字は `StringInfo` の text element 単位で数える。ZWJ の絵文字と国旗は途中の状態が見え、タイ語の SARA AM は 2 回に分けて表示されるが、Gallery の長文（ja、en、ar、th）では字形は崩れない。<br>Standard の生成器で使う部品は、`Glyph.textRange` と `parsedText` に頼らない（例外になる）。<br>確かめていないこと: Standard の生成器での `</noparse>` のエスケープ、Standard の生成器での ar と th |

## 既知の事項

- 別プロジェクトの Editor を同時に開いていると、この Editor の `com.unity.pipeline` サーバーに `unity command` が届かないことがある（`com.unity.pipeline` 0.7.0-exp.1、6000.7.0b2）。
  症状は、`unity status` のこの行が `unreachable` のまま、`unity command` が別プロジェクトの Editor（0.8.0-exp.1）に応答される。
  そのときは `netstat` で、この Editor が `0.0.0.0:7800`、別の Editor が `127.0.0.1:7800` を同時に listen していた。原因は特定していない。
  回避策は、Pipeline のポートを空いている番号に固定すること。`Window/Pipeline/Settings` で作られる `Assets/Settings/Pipeline/EditorPipelineManager.asset` の `m_Port` に番号を入れて Editor を開き直すと、そのポートで応答する。
  このアセットは手元の設定なので `.gitignore` に入れてある。
  このアセットを新規に置いた最初の Editor の起動では読まれない（アセットのインポートが Pipeline の起動より後になる）。もう一度開き直す。
- `LegacyRuntime.ttf`（Unity 組み込み）からは `FontAsset` を作れない。`FontAsset.CreateFontAsset(font, ...)` が `null` を返し
  `Unable to load font face for [LegacyRuntime]. Make sure "Include Font Data" is enabled in the Font Import Settings.` の警告が出る（6000.7.0b2）。
  検証用の `FontAsset` は `Fonts/` の TTF から作る。
- Editor の Play Mode で描いた文字の glyph と atlas は、Dynamic の FontAsset のアセットに書き込まれて残る（atlas 1 枚で 1 つが MB 単位になる）。
  Gallery や Experiments を Play Mode で回した後は、`Sandbox/Fonts/*.asset` と `Experiments/RoomExp/Fonts/*.asset` に差分が出る。
  コミットの前に、Play Mode を抜けてから、差分の出た各 FontAsset の `ClearFontAssetData(true)`（公開 API）を `unity command eval_file` で呼んで消す（Inspector の Clear Dynamic Data と同じ目的）。
  消すと atlas は 1×1 に戻るが、atlas の Filter Mode は残り、次に描いたときに作り直される（6000.7.0b2）。
- `capture_game_view` の `--width` `--height` は、Game view が描画した画像を拡大・縮小するだけで、その解像度で描画し直すわけではない（6000.7.0b2）。
  Game view の描画解像度がウィンドウの大きさのままだと、文字のにじみや 1px の線を判定できない画像になる。画素を見る撮影では、先に `UnityEditor.PlayModeWindow.SetCustomRenderingResolution(<幅>, <高さ>, <名前>)` で描画解像度を撮影の大きさに合わせる。
- SVG は、パッケージを足さなくても Unity 内蔵の SVG importer（`UnityEditor.VectorGraphicsModule` の `SVGImporter`）で import される。既定は UI Toolkit の Vector Image（.meta の `svgType: 3`）。
  ただし Vector Image を `Hone.Button` の `background-image` にすると、そのボタンの文字が描かれなかった（`:focus` のときだけ付けた場合。画像は出る。6000.7.0b2、Screen Space。World Space は未確認）。
  同じ SVG を Texture2D として import する（`svgType: 2`、大きさは `textureWidth` / `textureHeight`）と、画像と文字の両方が出た。`background-image` に使う SVG は Texture2D で import する。
- DotGothic16 を 16 の整数倍でない大きさ（28px）で描くとき（Standard の生成器、6000.7.0b2）:
  16px 用の `DotGothic16.asset`（sampling 16）を引き伸ばすと、字の横や下に別の字のかけらが出て字形が崩れた。TTF を `FontDefinition.FromFont` で直に指すと、崩れはないが縁がにじんだ。
  その大きさで直接ラスタライズする FontAsset（`DotGothic16-28.asset`。RASTER_HINTED、sampling 28、padding 1、atlas 1024、Dynamic、multi atlas 無効、atlas は Point）を作ると、くっきり描けた。
  作り方は HoneSync の `LoadOrCreateFontAsset` と同じ（`FontAsset.CreateFontAsset` の後に `CreateAsset`、atlas と material をサブアセットとして足す）
- 行の間隔は USS では指定できないが、FontAsset の行の高さ（`faceInfo.lineHeight`。Inspector の Line Height）で変えられる。2 行目の位置は 1 行目から lineHeight だけ下がる。
  `DotGothic16-28.asset` は 40.544 を 35.5 に変えた（テキストアドベンチャー風の参考の行の間隔に合わせた。6000.7.0b2、Standard の生成器）
- Standard の生成器で描く Label に `overflow: hidden` があると、文字の位置は正しいのに文字が丸ごと描かれなかった（MessageWindow の名札。`text-overflow` や `letter-spacing` を変えても同じで、`overflow: visible` で出た。6000.7.0b2）。
  Advanced の生成器に切り替えると、箱の下端の外にだけ文字のかけらが見えた
- USS の `z-index` は 6000.7.0b1 で入った（警告なしで読み込まれる）。ただし子要素に `z-index: -1` を付けても、親の枠（border）より奥には描かれなかった。
  MessageWindow の名札（子、`position: absolute`）に付けて、窓の上の枠と重なる部分の画素が、付けないときと完全に同じだった（6000.7.0b2、Screen Space）。
  子を親の枠より奥に見せたいときは、子の画像に親の枠と同じ線を描き込む。兄弟どうしの重なり順に効くかは確かめていない
- `Hone.Core` / `Hone.Core.Editor` は `.cs` が 1 本も無い間は Unity がアセンブリを生成しない。
  `Core/` に最初のスクリプトが入った時点で `Library/ScriptAssemblies/` に現れる。
  `Hone.Core.Editor` は asmdef の `includePlatforms` が `Editor` のみなので、Player ビルドには含まれない。
