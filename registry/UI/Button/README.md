# Button

`UnityEngine.UIElements.Button` を継承した、トークン参照だけのボタン。`clicked`、`text`、`NavigationSubmitEvent` の処理は基底のまま使える。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:hone="Hone">
    <hone:Button variant="outline" text="Save" />
</ui:UXML>
```

```csharp
var button = new Hone.Button { text = "Save", variant = Hone.Button.Variant.Outline };
button.clicked += OnSave;
```

## variant

| variant | USS クラス | 用途 |
|---|---|---|
| `Default` | `hone-button` のみ | 主となる操作 |
| `Outline` | `hone-button--outline` | 枠だけの操作 |
| `Ghost` | `hone-button--ghost` | 背景も枠も無い操作 |
| `Destructive` | `hone-button--destructive` | 取り消せない操作 |

`variant` を変えると、前の variant のクラスが外れて新しいクラスが付く。

## 名前の衝突

`UnityEngine.UIElements` にも `Button` がある。同じファイルで `using UnityEngine.UIElements;` と Hone の `Button` を併用するときは、エイリアスで解く。

```csharp
using UnityEngine.UIElements;
using Button = Hone.Button;
```

`variant` のクラスは `variant` プロパティで管理する。UXML の `class` 属性やクラスリストの直接操作で `hone-button--*` を付けると、`variant` と見た目が食い違う。

## headless 層との関係

- `hone-focusable`（`Core.uss`）を付けてあり、フォーカス時の ring は `Core.uss` が描く。Button 側では何も書かない。
- `hone-text`（`Core.uss`）を付けてあり、本文フォントに従う。
- `unity-button` クラスは外してあり、既定テーマの Button の見た目は効かない。
- `FocusScope` や `BackStack` は使わない。単独で完結する。

## World Space での置き方

World Space の panel でも、Screen Space と同じ構造のまま置ける。Button 側に World Space 用の設定は無い。
Unity 6000.7.0b2、Windows の Player で、World Space の panel（1920×1080、`worldSpaceSizeMode = Fixed`）の中央に置いた既定の variant に、マウスのポインタを `Camera` 経由で当てて確かめた。
scene には EventSystem（`InputSystemUIInputModule`）と、`processWorldSpaceInput = true` の `PanelInputConfiguration`（event camera は Main Camera）がある。

| 操作 | 結果 |
|---|---|
| ポインタを乗せる（`:hover`） | `opacity` が 1 から 0.9 になる |
| 押している間（`:active`） | `opacity` が 0.8 になる |
| クリックした後（`:focus`） | `focusController.focusedElement` が Button になり、`border-width` が 1px から 2px（`--hone-ring-width`）になる |
| 押して離す | `clicked` が 1 回発火する |

2 枚目の World Space の panel（480×300）に開いた Dialog があっても、1 枚目の Button の `clicked` は発火する。panel をまたいで入力は止まらない。
別の panel の Dialog を `Open()` すると、この Button はフォーカスを失い、Dialog を `Close()` しても戻らない（別の panel の要素がフォーカスを得ると、元の panel のフォーカスは外れた。詳しくは Dialog の README）。

## レイアウト

`width` は固定していない。`min-width` と内容で決まり、`flex-shrink: 0` なので横並びの親の中では縮まない。長い文字列を省略記号で切りたいときは、親を縦並び（`align-items: stretch`）にして幅を決める。
