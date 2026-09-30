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

## レイアウト

`width` は固定していない。`min-width` と内容で決まり、`flex-shrink: 0` なので横並びの親の中では縮まない。長い文字列を省略記号で切りたいときは、親を縦並び（`align-items: stretch`）にして幅を決める。
