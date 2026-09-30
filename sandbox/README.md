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
    Runtime/          Hone.Core.asmdef
    Editor/           Hone.Core.Editor.asmdef
  Tokens.uss          registry/Tokens.uss の手コピー
  HoneTheme.tss       registry/HoneTheme.tss の手コピー
  Sandbox/            sandbox 固有のアセット。`hone add` がコピーする領域と混ぜない
    Sandbox.unity     PanelRenderer を 1 つ置いたシーン（EventSystem は置かない）
    Sandbox.uxml      空の UXML
    PanelSettings.asset   Theme Style Sheet に HoneTheme.tss を割り当て済み
    Fonts/            検証用フォント。RobotoMono-Regular.ttf（Apache-2.0、Unity Editor 同梱）とその LICENSE、そこから作った Dynamic の FontAsset
    Experiments/<Name>/   Issue ごとの検証。FontVar/ は `-unity-font-definition` を USS 変数経由で差し替えられるかの検証
                      FocusTrap/ は Dialog の focus trap 機構（IgnoreEvent の同 frame 順序、初期フォーカス、EventSystem の有無）の検証
                      FontVar/Resources/Fonts/ は case 3（`resource()`）用の Fonts/RobotoMono.asset の複製。元を作り直したら同期する。
                      Resources 配下なので sandbox のすべての Player ビルドに入る
    Tests/PlayMode/   PlayMode テスト
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

## FontVar の Player 検証

`FontVar.unity` は Build Settings に入れていないので、ビルド時にシーンを指定する。`unity command build` は 30 秒でコマンド自体が timeout するが、ビルドは続くので `build_status` で完了を待つ。

```bash
unity command build --project-path <sandbox の絶対パス> --target StandaloneWindows64 --outputPath <sandbox の絶対パス>/Build/FontVar/FontVar.exe --scenes '["Assets/Hone/Sandbox/Experiments/FontVar/FontVar.unity"]' --confirm true
unity command build_status --project-path <sandbox の絶対パス>
<sandbox の絶対パス>/Build/FontVar/FontVar.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -fontvar-shot <png の絶対パス> -logFile <ログの絶対パス>
```

判定はログの `[FontVarProbe]` 行（Label ごとの `fontAsset`）で行う。スクリーンショットは補助。Probe は失敗時に `LogError` を出し、終了コード 1 で終了する。

## FocusTrap の Player 検証

`FocusTrap.unity`（EventSystem なし）と `FocusTrapEventSystem.unity`（EventSystem + `InputSystemUIInputModule` あり）は Build Settings に入れていないので、ビルド時にシーンを指定する。
`FocusTrapProbe` は Input System の合成デバイス（Gamepad、Keyboard）へ入力を積み、UI map の既定バインディングを通して全ステップを自動で回し、終了する。

```bash
unity command build --project-path <sandbox の絶対パス> --target StandaloneWindows64 --outputPath <sandbox の絶対パス>/Build/FocusTrap/FocusTrap.exe --scenes '["Assets/Hone/Sandbox/Experiments/FocusTrap/FocusTrap.unity"]' --confirm true
unity command build_status --project-path <sandbox の絶対パス>
<sandbox の絶対パス>/Build/FocusTrap/FocusTrap.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile <ログの絶対パス>
```

EventSystem あり側は `FocusTrapEventSystem` に読み替える。判定はログの `[FocusTrapProbe] RESULT` 行（1 ステップ 1 行）で行う。
`start` が押す前のフォーカス、`final` が 8 frame 後のフォーカス、`settled=+N` が最終状態に落ち着いた frame（押した frame を +0）。
`outsideAtFrameEnd` は frame 終端で外側の要素にフォーカスがあった frame の有無、`outsidePainted` は外側の要素がフォーカス色で描画された frame の有無。

## 既知の事項

- 複数の Editor を同時に開いていると、`com.unity.pipeline` のサーバーが同じポート（7800）を取り合い、`unity status` が `unreachable` のまま `unity command` が別プロジェクトの Editor に届く。
  `Assets/Settings/Pipeline/EditorPipelineManager.asset`（`m_Port`）で空きポートを固定すると届く。このアセットはコミットしない。

- `LegacyRuntime.ttf`（Unity 組み込み）からは `FontAsset` を作れない。`FontAsset.CreateFontAsset(font, ...)` が `null` を返し
  `Unable to load font face for [LegacyRuntime]. Make sure "Include Font Data" is enabled in the Font Import Settings.` の警告が出る（6000.7.0b2）。
  検証用の `FontAsset` は `Fonts/` の TTF から作る。
- `Hone.Core` / `Hone.Core.Editor` は `.cs` が 1 本も無い間は Unity がアセンブリを生成しない。
  `Core/` に最初のスクリプトが入った時点で `Library/ScriptAssemblies/` に現れる。
  `Hone.Core.Editor` は asmdef の `includePlatforms` が `Editor` のみなので、Player ビルドには含まれない。
