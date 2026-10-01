# Hone

Unity UI Toolkit 向けの、shadcn/ui 方式の UI コンポーネント集。

- 振る舞い（フォーカス、ゲームパッドナビゲーション、dismiss）は headless 層が担う
- 見た目はトークンだけで組まれた薄い既定を持ち、利用者が完全に握る
- CLI で `Assets/` 配下にコピーし、以後は利用者自身のコードになる
- AI エージェントを第一の利用者として、全てをテキスト（UXML / USS / C#）で構成する

対象は Unity 6.7 LTS 以降の UI Toolkit。設計規約は [AGENTS.md](AGENTS.md) を参照。

現在は設計と検証の段階で、まだ利用できるコンポーネントはない。以下のフローは設計上の目標であり、
「未検証」と記した箇所は sandbox での確認を待っている。

## Unity への組み込みフロー

Web の shadcn は `npx shadcn add button` でファイルが落ちてきて、それを `import` すれば終わる。
Unity で同じ体験を作るとき、構造的に違う点が二つある。

1. **Unity はファイルを「取り込む」工程を持つ。** CLI が書いたファイルは、Unity Editor がフォーカスを取り戻した時に
   import とコンパイルが走ってから使える。Web のホットリロードに相当する待ちが一回入る。
2. **設定に二種類ある。** `.uss` `.uxml` `.tss` はテキストなので CLI が直接書ける。一方 `PanelSettings`
   `PanelTextSettings` `FontAsset` は Unity のアセットで、Unity の API でしか正しく作れない。
   Hone はこれを「CLI がテキストを置き、Unity 側の `Hone > Sync` がアセットに変換する」という二段で扱う。

### 前提

- Unity 6.7 LTS 以降のプロジェクト
- UI を表示する `PanelSettings` アセットと、シーン上の `PanelRenderer`（6.5 以降。旧 `UIDocument` でも動く）
- Input System パッケージ（ゲームパッド入力のため。6.7 では標準同梱）
- Node.js 20.12 以上（CLI 実行のため。生成物には残らない）

### 1. 初期化

Unity プロジェクトのルート（`Assets/` と `ProjectSettings/` がある階層）で実行する。

```bash
npx @kaibutsu50/hone init
```

CLI が行うこと。

- `hone.json` を作る。出力先（既定 `Assets/Hone`）と registry の取得元（URL かローカルパス。`--registry <URL|パス>` で指定する）を記録する。`hone.json` が既にあれば書き換えず、その内容を使う
- `Assets/Hone/Core/` に headless 層をコピーする。asmdef はここにだけ入る（Runtime 用の `Hone.Core` と Editor 用の `Hone.Core.Editor`）
- `Assets/Hone/Tokens.uss` にトークン定義をコピーする
- `Assets/Hone/HoneTheme.tss` をコピーする。Unity 既定テーマを `@import` し、その後に Hone のトークンと Core の USS を `@import` する
- `Assets/Hone/hone.manifest.json` を作る。Unity 側の Sync が読む台帳
- 既にあるファイルは上書きしない。`init` は何度実行しても何も壊れない

Unity Editor に戻ると import とコンパイルが走る。その後、メニューの `Hone > Sync` を一度実行する。

Sync が行うこと。

- `Assets/Hone/hone.manifest.json` を読む。無ければ `init` を案内して何もしない（`hone.json` の `output` を変えても、Sync が読むのはこの場所）
- `PanelTextSettings` アセットを `Assets/Hone/HonePanelTextSettings.asset` に作り、プロジェクトの `PanelSettings` の Text Settings に割り当てる
- `HoneTheme.tss` を同じ `PanelSettings` の Theme Style Sheet に割り当てる
- `PanelSettings` の他の設定（scale mode、sort order など）は変えない。既に設定済みの項目は飛ばすので、何度実行してもよい

対象は `Assets/` 配下の `PanelSettings`。1 つならそのまま適用し、無ければ「PanelSettings がありません」と出して終わる。複数ある場合は Sync が一覧を出し、どれに適用するかチェックボックスで選ぶ（既定はどれも選ばれていない）。
エラーがあったときは、件数をダイアログで知らせる（内容は Console に出る）。

### 2. コンポーネントの追加

```bash
npx @kaibutsu50/hone add Button Dialog
```

CLI が行うこと。

- `Assets/Hone/UI/Button/` に `Button.cs` `Button.uxml` `Button.uss` `README.md` をコピーする
- `registryDependencies` を解決し、依存先も一緒にコピーする（`Dialog` を追加すると `Button` が先にコピーされる。ファイルが全部ある依存先はコピーしない）
- `HoneTheme.tss` に `@import url("UI/Button/Button.uss");` を挿入する。位置は最後の `@import` 行の直後で、既定の `HoneTheme.tss` では利用者が書く `:root` などの規則より前になる。パスは `HoneTheme.tss` からの相対で書く（出力先を変えても壊れない）。これで USS はプロジェクト全体に効く。同じ USS を指す `@import` が既にあれば挿入しない
- `hone.manifest.json` の `components` に追加した名前を追記する
- 項目が UPM パッケージを必要とするとき、`Packages/manifest.json` の `dependencies`（直接の依存）に無ければ警告する（インストールはしない）
- コピーするコンポーネントのファイルは、既にあれば上書きしない（飛ばした旨を表示する）。編集済みのファイルを残したまま何度でも実行できる。`add` が書き換えるのは `HoneTheme.tss`（`@import` 行の挿入だけで、既存の行は変えない）と `hone.manifest.json`（`components` の追記）だけ
- 名前は大文字小文字を区別しない（`button` でも `Button` でもよい）

`npx @kaibutsu50/hone add` を引数なしで実行すると、追加できるコンポーネントの一覧を表示する。

Unity に戻ってコンパイルが終われば使える。ここで Sync は不要。

### 3. 使う

UXML から。ルート要素に `xmlns:hone="Hone"` を足し、タグは `<hone:Button>` と書く。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:hone="Hone">
  <hone:Button name="start" text="はじめる" class="hone-button--outline" />
</ui:UXML>
```

C# から。クラス名は `Hone.Button`。

```csharp
using Hone;

var start = root.Q<Button>("start");
start.clicked += () => Debug.Log("start");
```

同じファイルで `UnityEngine.UIElements` も `using` していると `Button` が曖昧になる。
その時は `using Button = Hone.Button;` のエイリアスを書くか、Unity 側を完全修飾で書く。

UI Builder からは、Library の Project タブに `Hone` 配下として現れる。

見た目を変えたいときは、`Assets/Hone/UI/Button/Button.uss` を直接編集する。
それは利用者のファイルであり、Hone は二度と上書きしない。
トークン（色、角丸、余白）をまとめて変えたいときは `Assets/Hone/Tokens.uss` を編集する。

### 4. フォントの追加

日本語・中国語・韓国語（CJK）を表示するプロジェクトでは必須の手順。Unity 6.5 以降の Advanced Text Generator は、
fallback が空のまま未収録文字に当たると OS フォントの全列挙が走り、フリーズする報告がある。

```bash
npx @kaibutsu50/hone add font ja
```

CLI が行うこと。

- Noto Sans JP（OFL）を GitHub のリリース（タグ固定）から取得し、`Assets/Hone/Fonts/NotoSansJP/` に Regular と Bold の `.otf` とライセンス（`LICENSE.txt`）を置く
- `hone.manifest.json` の `fonts` に項目を追記する
- 取得したファイルは `~/.cache/hone/fonts/` に残す。キャッシュがあればネットワークなしで置ける。配置先に 3 ファイル（Regular、Bold、`LICENSE.txt`）が名前で全部揃っていれば取得しない（内容は比べない。既にあるファイルは上書きしない）

Unity に戻り、`Hone > Sync` を実行する。Sync が行うこと。

- 置かれたフォントから Dynamic モードの `FontAsset` を、フォントと同じディレクトリに `<フォントのファイル名> SDF.asset` として生成する（既にあれば作らない）
- `HonePanelTextSettings.asset` の Fallback Font Assets に、`hone.manifest.json` の `fonts` の順で追加する（既定フォントは変えない）

`ko`（Noto Sans KR）`zh-hans`（Noto Sans SC）`zh-hant`（Noto Sans TC）`ar`（Noto Sans Arabic）`th`（Noto Sans Thai）も同じ手順。`lang` の大文字小文字は区別しない。`add font ja ko` のように複数を一度に指定できる。複数を足すとフォールバック連鎖になる。

### 5. 本文フォントの指定

Hone はフォントを同梱しない。`.hone-text` クラスを付けた要素は `--hone-font-body` のフォントで描かれる。
使う `FontAsset` は、`HoneTheme.tss` の `:root` に `url("project://…")` の形で書く（`HoneTheme.tss` のコメントに雛形がある）。
書かなければ、`.hone-text` は祖先のフォント指定を継承し、祖先にも無ければ Unity の既定フォントになる。警告は出ない。

- UI Builder の canvas は、既定テーマのままだと `HoneTheme.tss` の `:root` の変数を反映しない。UI Builder ではフォントが変わって見えないので、Play Mode で確認する
- `FontAsset` は TTF から作る。Unity 組み込みの `LegacyRuntime.ttf` からは作れない

### Web との対応表

| Web (shadcn) | Unity (Hone) |
|---|---|
| `npx shadcn init` が `tailwind.config` と `globals.css` を書く | `npx @kaibutsu50/hone init` が `HoneTheme.tss` を書き、`Hone > Sync` が `PanelSettings` に割り当てる |
| `npx shadcn add button` | `npx @kaibutsu50/hone add Button` |
| `import { Button } from "@/components/ui/button"` | UXML は `xmlns:hone="Hone"`、C# は `using Hone;` |
| スタイルはコンポーネント内の Tailwind クラス | スタイルは `Button.uss`。`HoneTheme.tss` から `@import` される |
| `next/font` でフォントを取得 | `npx @kaibutsu50/hone add font ja` と `Hone > Sync` |
| ホットリロード | Unity Editor に戻った時の import とコンパイル |

### 未検証の点

- Sync を Editor のメニューではなく CLI から起動できるか（Unity の batchmode 経由）
