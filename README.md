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
- Node.js（CLI 実行のため。生成物には残らない）

### 1. 初期化

Unity プロジェクトのルート（`Assets/` がある階層）で実行する。

```bash
npx hone init
```

CLI が行うこと。

- `hone.json` を作る。出力先（既定 `Assets/Hone`）と registry の URL を記録する
- `Assets/Hone/Core/` に headless 層をコピーする。asmdef はここにだけ入る（Runtime 用の `Hone.Core` と Editor 用の `Hone.Core.Editor`）
- `Assets/Hone/HoneTheme.tss` を作る。Unity 既定テーマを `@import` し、その後に Hone のトークンと Core の USS を `@import` する
- `Assets/Hone/hone.manifest.json` を作る。Unity 側の Sync が読む台帳

Unity Editor に戻ると import とコンパイルが走る。その後、メニューの `Hone > Sync` を一度実行する。

Sync が行うこと。

- `PanelTextSettings` アセットを `Assets/Hone/` に作り、プロジェクトの `PanelSettings` に割り当てる
- `HoneTheme.tss` を同じ `PanelSettings` の Theme Style Sheet に割り当てる

`PanelSettings` が複数ある場合は Sync が一覧を出し、どれに適用するか選ぶ。

### 2. コンポーネントの追加

```bash
npx hone add Button Dialog
```

CLI が行うこと。

- `Assets/Hone/UI/Button/` に `Button.cs` `Button.uxml` `Button.uss` `README.md` をコピーする
- `registryDependencies` を解決し、依存先も一緒にコピーする
- `HoneTheme.tss` に `@import url("/Assets/Hone/UI/Button/Button.uss");` を追記する。これで USS はプロジェクト全体に効く

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

日本語を表示するプロジェクトでは必須の手順。Unity 6.5 以降の Advanced Text Generator は、
fallback が空のまま未収録文字に当たると OS フォントの全列挙が走り、フリーズする報告がある。

```bash
npx hone add font ja
```

CLI が行うこと。

- Noto Sans JP（OFL）を取得し、`Assets/Hone/Fonts/NotoSansJP/` にライセンスごと置く
- `hone.manifest.json` にフォントの項目を追記する

Unity に戻り、`Hone > Sync` を実行する。Sync が行うこと。

- 置かれたフォントから Dynamic モードの `FontAsset` を生成する
- `PanelTextSettings` の Fallback Font Assets に追加する

`ko` `zh-hans` `zh-hant` `ar` `th` も同じ手順。複数を足すとフォールバック連鎖になる。

### Web との対応表

| Web (shadcn) | Unity (Hone) |
|---|---|
| `npx shadcn init` が `tailwind.config` と `globals.css` を書く | `npx hone init` が `HoneTheme.tss` を書き、`Hone > Sync` が `PanelSettings` に割り当てる |
| `npx shadcn add button` | `npx hone add Button` |
| `import { Button } from "@/components/ui/button"` | UXML は `xmlns:hone="Hone"`、C# は `using Hone;` |
| スタイルはコンポーネント内の Tailwind クラス | スタイルは `Button.uss`。`HoneTheme.tss` から `@import` される |
| `next/font` でフォントを取得 | `npx hone add font ja` と `Hone > Sync` |
| ホットリロード | Unity Editor に戻った時の import とコンパイル |

### 未検証の点

- `.tss` からの `@import` でプロジェクト絶対パスの USS を読めること
- `--hone-font-body` に FontAsset を入れ、`-unity-font-definition: var(--hone-font-body)` で参照できること。
  動作報告はあるが公式ドキュメントにない。通らない場合は C# 側の注入口を既定にする
- Sync を Editor のメニューではなく CLI から起動できるか（Unity の batchmode 経由）
