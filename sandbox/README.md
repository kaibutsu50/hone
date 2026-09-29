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

## 既知の事項

- `Hone.Core` / `Hone.Core.Editor` は `.cs` が 1 本も無い間は Unity がアセンブリを生成しない。
  `Core/` に最初のスクリプトが入った時点で `Library/ScriptAssemblies/` に現れる。
  `Hone.Core.Editor` は asmdef の `includePlatforms` が `Editor` のみなので、Player ビルドには含まれない。
