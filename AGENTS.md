# Hone

Unity UI Toolkit 向けの、shadcn/ui 方式の UI コンポーネント集。
振る舞い（フォーカス・ナビゲーション・dismiss）は基盤が担い、見た目の主導権は利用者が持つ。
配布は UPM ではなく、CLI で利用者の `Assets/` 配下にコピーし、以後は利用者自身のコードになる。

第一の利用者は AI エージェントである。このファイルは、エージェントがこのリポジトリで正しく作業するための正典である。

## 対象と前提

- UI Toolkit のみ。uGUI は対象外。
- 基準バージョンは Unity 6.7 LTS。6.7 LTS が出るまでは 6000.7 系の最新 alpha で検証する。Unity 7 は 6.7 の直接の継続と公式に宣言されているため、6.7 対応が Unity 7 対応を意味する。
- 平面のランタイム UI を対象とする。Editor 拡張は対象外。XR 固有の対応は後回しだが、World Space で壊れる設計は禁止する（後述の DO NOT を参照）。
- Advanced Text Generator がランタイム既定である前提で組む。static な FontAsset は存在しないものとして扱う。

## リポジトリ構成

```
AGENTS.md                  このファイル
registry.json              shadcn 互換の配布定義。形式は「registry.json の形式」節
registry/                  中身は利用者側の Assets/Hone/ と同じ構造
  Core/                    headless 層。全コンポーネントが依存する
  UI/<Name>/               プリミティブ。<Name>.cs <Name>.uxml <Name>.uss README.md の4点セット
  Blocks/<Name>/           プリミティブの合成で作る画面単位のパターン
  Tokens.uss               トークン定義
  HoneTheme.tss            テーマの雛形。Unity 既定テーマ、Tokens.uss、Core の USS、追加した各コンポーネントの USS を @import する
cli/                       hone CLI
sandbox/                   検証用 Unity プロジェクト。多言語スクリーンショット基盤を含む
```

利用者のプロジェクトでは、`registry/` 配下が出力先（既定 `Assets/Hone/`、`hone.json` で変えられる）へ同じ相対構造でコピーされる。asmdef は Core にだけ置く（Runtime 用の `Hone.Core` と Editor 用の `Hone.Core.Editor`）。UI と Blocks は利用者側の asmdef に乗る。

## registry.json の形式

トップレベルは shadcn の `registry.json` と同じ。`$schema` `name`（`hone`）`homepage` `items` を持つ。

`items` の各項目のフィールド:

| フィールド | 型 | 意味 |
|---|---|---|
| `name` | string | CLI で指定する名前。PascalCase で、ディレクトリ名・クラス名と同じ（`Button`, `Core`, `PauseMenu`） |
| `type` | `registry:ui` / `registry:block` / `registry:lib` / `registry:theme` | 項目の種別。配置先は決めない |
| `title` | string | 表示名（`Button`） |
| `description` | string | 一行説明 |
| `files` | `{ path, type }[]` | `registry/` からの相対パス。出力先からの相対パスでもある。`type` は項目の type と同じ |
| `registryDependencies` | string[] | 先にコピーが必要な Hone の項目名。ui は必ず `Core` を含む |
| `dependencies` | string[] | 必要な UPM パッケージ名（`com.unity.render-pipelines.universal` 等）。CLI は検出して警告するだけで、インストールはしない |
| `classes` | string[] | 項目が定義する C# クラス名（`Hone.Button`）。将来の `--prefix` 用。今は記録するだけ |

- shadcn の `tailwind` `cssVars` は持たない。Unity には対応物がない。
- `files[].target` は持たない。配置先は出力先と `files[].path` をつなげたものに決まる。
- リポジトリ内に独自の JSON Schema ファイルや検証スクリプトは置かない。`$schema` は shadcn のものを参照するだけ。

## コンポーネントと block の境界

判定基準は一つ。「既存プリミティブの組み合わせでは供給できない headless な振る舞いを持つか」。
持つならプリミティブとして `registry/UI/` に置く。持たないなら block として `registry/Blocks/` に置く。
block は必ず `registryDependencies` でプリミティブを宣言し、自前で振る舞いを実装しない。

## 命名規約

- C# namespace は `Hone`。headless 層は `Hone.Core`。
- C# クラス名は shadcn と同じ短い名前にする（例: `Hone.Button`）。prefix は付けない。
- UXML タグ名はクラス名そのままで、`xmlns:hone="Hone"` を宣言して `<hone:Button>` と書く。
- `UnityEngine.UIElements` にも `Button` `Toggle` `Slider` 等の同名クラスがある。同じファイルで両方の namespace を `using` すると曖昧参照エラーになる。衝突したファイルでは `using Button = Hone.Button;` のエイリアスを書くか、Unity 側を完全修飾で書く。Hone のクラス定義自身は基底クラスを完全修飾で書く（例: `class Button : UnityEngine.UIElements.Button`）。
- USS クラスは BEM 風。ブロックは `.hone-button`、variant は `.hone-button--outline`。状態は pseudo-class（`:focus` `:disabled` `:hover`）を優先し、pseudo-class で表せない状態のみ `.is-open` 形式のクラスを使う。
- トークンは `--hone-color-*` `--hone-radius-*` `--hone-space-*` `--hone-font-*`。prefix は利用者の変数との衝突回避のため。
- ファイル名はクラス名と一致させる。1 コンポーネント 1 ディレクトリ。

## スタイルの規約

- コンポーネントの USS はトークンを `var()` で参照するだけで構成する。色・角丸・余白・フォントの実値を書かない。
- `HoneTheme.tss` の `@import` は、`HoneTheme.tss` からの相対パスで書く。出力先を変えても壊れず、絶対パス（`/Assets/Hone/…`）は出力先に依存する。
- 未解決の `var()` は、前の規則の値に戻らず、そのプロパティの初期値になる（継承するプロパティは祖先の値を引き継ぐ）。トークンの定義漏れは、見た目が黙って崩れる形で現れる。
- テーマ内（`Core.uss` やコンポーネントの USS、`HoneTheme.tss` 自身）に書いた「クラス 1 つ + pseudo-class」の規則は、既定テーマ（`unity-theme://default`）の Button の `:focus` に勝てない。
  focus ring は `.hone-focusable` を 2 回書いて詳細度を上げている。既定テーマの規則に負ける見た目が出たら、`resolvedStyle` で実際の値を測ってから詳細度を疑う。
- USS の `var()` は他の関数の中に書けない。`calc()` も存在しない。合成が必要な値はトークン側で完成形として定義する。
- 影とぼかしは `filter: drop-shadow()` と `backdrop-filter: blur()` を使う。`backdrop-filter` は URP のみ、Screen Space のみ、transition 非対応なので、必須の見た目に使わず opt-in の variant に留める。
- `line-height` `font-weight` `font-feature-settings` は USS プロパティとして存在しない。これらを前提にした API を作らない。

## フォントと多言語の規約

- フォントを同梱しない。本文フォントは `Core.uss` の `.hone-text` が `-unity-font-definition: var(--hone-font-body)` で参照する。
- `--hone-font-body` は利用者の FontAsset を指す値なので、`Tokens.uss` に既定値を置かない。利用者が `HoneTheme.tss` の `:root` に `url("project://…")` の形で書く。
  未定義のときは、`.hone-text` は祖先から継承したフォントに、祖先にも無ければ Unity の既定フォントになる。警告は出ず、黙って落ちる。
- USS 変数経由のフォント指定は公式には未文書化だが、6000.7 系の Editor と Windows Player で動くことを sandbox で確認済み。これを唯一の経路とし、C# の注入口（`Theme.SetBodyFont` 等）は作らない。
  UI Builder の canvas は既定テーマのままだと theme の `:root` の変数を反映しないので、UI Builder で見た目を確認する利用者にはこの制限を README で案内する。
- fallback が空の状態で未収録文字に当たると OS フォントの全列挙が走り、フリーズする報告がある。CJK を表示するプロジェクトでは `hone add font <lang>` で fallback を追加することを必須手順として案内する。
- レイアウトの規約。テキストを含む要素の幅を固定しない。ボタンは min-width と flex で伸ばす。行高はスクリプトごとに変わる前提で余白を組む。切り詰めは省略記号。
- 各スクリプトのテスト文字列（日本語、한국어、中文、العربية、ไทย、絵文字、長いドイツ語）で全コンポーネントのスクリーンショットを撮る。見た目の変更を伴う PR にはこれを添付する。

## headless 層の責務

UI Toolkit のランタイムが既定でやらないことが、そのまま `Hone.Core` の責務である。

- subtree に限定したフォーカス移動と、外へ出さない trap。
- Dialog を開いた時の初期フォーカスと、閉じた時の元の要素への復元。
- Cancel 操作で一段戻る意味付け。panel 単位のスタックで最前面の scope が処理する。
- 起動時と再表示時の初期フォーカス。
- `:focus` に対する一貫した見た目。
- variant を切り替える小さなヘルパー。

既定の 2D ナビゲーションは panel 全域を走査し、内部実装は差し替えできない。介入する手段は `NavigationMoveEvent` で `FocusController.IgnoreEvent` を呼んでから自前で `Focus()` する形のみ。フォーカス変更は非同期なので、同 frame での確定を前提にしない。

## DO NOT

- `UIDocument` と `rootVisualElement` に依存しない。6.5 以降 `PanelRenderer` が正であり、ライブラリは `VisualElement` と `AttachToPanelEvent` の層で完結させる。
- `Screen.width` `Screen.height` `Screen.dpi` を参照しない。World Space では意味を持たない。
- Input System を直接読まない。入力は Navigation イベントと Pointer イベントからのみ受け取る。scene に EventSystem がある場合とない場合で入力経路が変わるため、両方で動く必要がある。
- `UxmlTraits` `UxmlFactory` を使わない。6.6 で削除済み。`[UxmlElement]` と `[UxmlAttribute]` のみ。
- コンポーネントの USS に実値を書かない。トークン参照のみ。
- block に振る舞いを実装しない。必要なら振る舞いをプリミティブに切り出す。
- 「便利そうだから」で API を増やさない。shadcn と同じく、利用者がコピーして自分で書き換える前提なので、薄いほど価値がある。

## 各コンポーネントに同梱するもの

- `<Name>.cs` `[UxmlElement]` 付きの partial class。
- `<Name>.uxml` 使用例。
- `<Name>.uss` トークン参照のみのスタイル。
- `README.md` 数行の説明、UXML の使用例、variant の一覧、headless 層との関係。

## 検証

- 未検証の Unity 挙動を前提に設計しない。ドキュメントが沈黙している事項は sandbox で実験し、結果を Issue か PR に記録する。
- Unity のバージョン差で挙動が変わる。調査結果には対象バージョンを必ず添える。
