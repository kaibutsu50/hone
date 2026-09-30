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
    Runtime/          Hone.Core.asmdef、Core.uss、BackStack.cs（NavigationCancel で最前面の IDismissable を閉じる）、IDismissable.cs
    Editor/           Hone.Core.Editor.asmdef
  Tokens.uss          registry/Tokens.uss の手コピー
  HoneTheme.tss       registry/HoneTheme.tss の手コピー。差分は末尾の :root（--hone-font-body に Sandbox/Fonts/RobotoMono.asset を指定）だけ。
                      利用者が雛形のコメントに従って自分で書く内容に当たる。registry 側を変えたら、この :root を残して同期する
  Sandbox/            sandbox 固有のアセット。`hone add` がコピーする領域と混ぜない
    Sandbox.unity     PanelRenderer を 1 つ置いたシーン（EventSystem は置かない）
    Sandbox.uxml      空の UXML
    PanelSettings.asset   Theme Style Sheet に HoneTheme.tss を割り当て済み
    Fonts/            検証用フォント。RobotoMono-Regular.ttf（Apache-2.0、Unity Editor 同梱）とその LICENSE、そこから作った Dynamic の FontAsset
    Experiments/<Name>/   Issue ごとの検証。FontVar/ は `-unity-font-definition` を USS 変数経由で差し替えられるかの検証
                      FontVar/Resources/Fonts/ は case 3（`resource()`）用の Fonts/RobotoMono.asset の複製。元を作り直したら同期する。
                      Resources 配下なので sandbox のすべての Player ビルドに入る
                      FocusTrap/ は Dialog の focus trap 機構（IgnoreEvent の同 frame 順序、外側へのフォーカス漏れ、フォーカスが無いときの方向入力、EventSystem の有無）の検証
                      TokensExp/ は --hone-font-body（theme の :root）と .hone-text、.hone-focusable の ring の検証（Player の検証を含む）
                      ThemeExp/ は .tss の @import（絶対パス、相対パス）、既定テーマの Button の :focus、既定フォントの FontAsset と日本語の描画の検証
    Gallery/          多言語スクリーンショットの撮影基盤。TestStrings.json（スクリプトごとの短文・長文）、Gallery.unity / Gallery.uxml / Gallery.uss、GalleryController、
                      GalleryHoneEntries（Core.uss のクラスを付けた列を登録する）、GalleryFocus（ring を写すため最初の .hone-focusable にフォーカスを当てる）、
                      Hone.Sandbox.Gallery.asmdef（Tests/PlayMode が参照する）
    Tests/PlayMode/   PlayMode テスト。BackStackTests は PanelRenderer を GameObject で作って panel を得る（Editor で実行する前提）
```

`registry/` の内容は手でコピーしている。`hone add` ができたら CLI に置き換える。
シンボリックリンクや `file:` 参照では取り込まない。

## パッケージ

- Input System: 6.7 では core package（`com.unity.inputsystem` 6.7.0）
- Test Framework 1.9.0、UI Test Framework 6.7.0（`com.unity.ui.test-framework`）。どちらも Editor 同梱
- `com.unity.pipeline` 0.7.0-exp.1: `unity command` で Editor を操作するため。**0.8.0-exp.1 にしない。**
  0.8 は `[CliCommand]` を別アセンブリへ移しており、6000.7.0b2 同梱の URP Core（旧配置を前提）がコンパイルエラーになる

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

見た目が変わる PR に添付するスクリーンショットを撮る基盤。`GalleryController.Register(name, factory)` で登録したコンポーネントを、`Gallery/TestStrings.json` の全スクリプト × 短文・長文で並べる。1 コンポーネントが 1 列で、列の中は 1 スクリプト 1 行。
Unity 標準の `Label` と `Button` は `GalleryController` が自分で登録する（name は `"Label"` `"Button"`）。
`GalleryHoneEntries` が `Label.hone-text`（`.hone-text` を付けた `Label`）と `Button.hone-focusable`（`.hone-focusable` を付けた `Button`）を登録し、素の列と並べる。
`GalleryFocus` が UI の読み込み後に最初の `.hone-focusable` へフォーカスを当てるので、Play Mode で撮れば ring が写る。
背景と文字色は `Gallery.uss` が Hone のトークン（`--hone-color-background` `--hone-color-foreground`）で指定する。

Hone のコンポーネントを載せるときは、`Register` を **sandbox 側のファイル**（例: `Sandbox/Gallery/` 配下）から呼ぶ。`registry/` 配下のコードには書かない（配布物が sandbox の asmdef に依存してしまう）。
呼ぶ時点は `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]`。`GalleryController` が列を作るのは UI が読み込まれた時点の 1 回だけで、それより後の `Register` は画面に出ず、ログも出ない。
このプロジェクトは Domain Reload を無効にしているので、`Register` した内容は Play をまたいで残る。同じ name の再登録は置き換わる。テストなどで一時的に登録したものは `Unregister` で消す。

撮影手順（Editor は「Editor の開き方」で開いておく）:

```bash
unity command open_scene --path Assets/Hone/Sandbox/Gallery/Gallery.unity --project-path <sandbox の絶対パス>
unity command editor_play --project-path <sandbox の絶対パス>
MSYS_NO_PATHCONV=1 unity command capture_game_view --source screen --width 1920 --height 1080 --format json --project-path <sandbox の絶対パス> > <json の絶対パス>
python -c "import json,base64; t=open(r'<json の絶対パス>',encoding='utf-8').read(); d=json.loads(t[t.index('{'):]); open(r'<sandbox の絶対パス>/Screenshots/gallery.png','wb').write(base64.b64decode(d['data']['result']['base64']))"
unity command editor_stop --project-path <sandbox の絶対パス>
gh attach --key <ブランチ名> <sandbox の絶対パス>/Screenshots/gallery.png
```

- `screenshot --view game` は使わない。カメラ経由の撮影なので `PanelRenderer`（Screen Space）の UI が写らず、空と地面だけの画像が success で返る（6000.7.0b2）。
- `capture_game_view` は `--save_path` を渡さない。渡すと `Assets/` 配下（`sandbox/Assets/Screenshots/`）に保存され、Texture として import されて `.meta` も増える。返ってきた base64 を自分で書き出す。
- `--width` `--height` を省くと Game view のサイズで撮られる。Play Mode に入ってから撮る。
- `Screenshots/` は gitignore 済み。`gh attach`（`pr-screenshot` スキル）が返す markdown を PR 本文に貼る。

撮った画像で、文字列が読めるか、豆腐（□）になっていないか、行や列がはみ出していないかを人が見る。画像の自動比較はしない。

フォントは同梱しない。既定フォントと fallback で、ar、th、emoji などがどう描画されるかを見るのも、この画像の目的のひとつ。

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
- `Hone.Core` / `Hone.Core.Editor` は `.cs` が 1 本も無い間は Unity がアセンブリを生成しない。
  `Core/` に最初のスクリプトが入った時点で `Library/ScriptAssemblies/` に現れる。
  `Hone.Core.Editor` は asmdef の `includePlatforms` が `Editor` のみなので、Player ビルドには含まれない。
