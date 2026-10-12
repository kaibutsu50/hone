# Toggle

`UnityEngine.UIElements.Button` を継承した、押すたびに on / off が切り替わるボタン。shadcn の Toggle（default variant）に当たる。
`clicked`、`text`、`NavigationSubmitEvent` の処理は基底のまま使い、Toggle は on / off の `value` を足す。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:hone="Hone">
    <hone:Toggle text="Mute" />
    <hone:Toggle text="Grid" value="true" />
</ui:UXML>
```

```csharp
var mute = new Hone.Toggle { text = "Mute" };
mute.RegisterValueChangedCallback<bool>(evt => SetMuted(evt.newValue));
```

## API

| 名前 | 意味 |
|---|---|
| `value`（UXML は `value`） | on / off。既定 false。値が変わったときだけ `ChangeEvent<bool>` を 1 回出し、`.is-pressed` を付け外しする |
| `SetValueWithoutNotify(bool)` | `.is-pressed` を付け外しするが、`ChangeEvent` は出さない |

- 押すたびに `value` が反転する（クリックも、フォーカス時の `NavigationSubmitEvent` も基底の `clicked` 経由）。
- on / off の変化を受けるときは `RegisterValueChangedCallback<bool>` を使う。基底の `TextElement` が `INotifyValueChanged<string>` も実装しているので、型引数の `<bool>` を省くと型が決まらずコンパイルエラーになる（Unity 6000.7.0b4 で確認）。`clicked` も基底のまま出るが、押した事実を知らせるだけで、値の変化は伝えない。
- variant と size は持たない。

## USS クラス

| クラス | 意味 |
|---|---|
| `hone-toggle` | ブロック。off は背景が透明で、hover で `--hone-color-muted` |
| `hone-toggle` + `is-pressed` | on の間だけ付く。背景は `--hone-color-accent`（on のまま hover しても accent のまま） |
| `:disabled`（`SetEnabled(false)`） | `opacity: 0.5` |

on を `:checked` で表さないのは、`VisualElement.pseudoStates` が internal で、Button の派生からは `:checked` を付けられないため。

## 名前の衝突

`UnityEngine.UIElements` にも `Toggle` がある。同じファイルで `using UnityEngine.UIElements;` と Hone の `Toggle` を併用するときは、エイリアスで解く。

```csharp
using UnityEngine.UIElements;
using Toggle = Hone.Toggle;
```

`value` と `.is-pressed` は常に対になる。UXML の `class` 属性やクラスリストの直接操作で `is-pressed` を付けると、`value` と見た目が食い違う。

## ToggleGroup との使い分け

選択肢の中から選ぶ用途（セグメント切替、選択状態を持つリストの行）は `Hone.ToggleGroup` が担う。**ToggleGroup の子に Toggle は使わない**（子には `Hone.Button` を使う）。
Toggle 自身の on / off と、ToggleGroup の基底が付ける `:checked` の選択が二重になり、見た目と値が食い違う。Toggle は単独で on / off を持つボタン（ミュート、表示の切り替えなど）に使う。

## headless 層との関係

- `hone-focusable`（`Core.uss`）を付けてあり、フォーカス時の ring は `Core.uss` が描く。Toggle 側では何も書かない。
- `hone-text`（`Core.uss`）を付けてあり、本文フォントに従う。
- `unity-button` クラスは外してあり、既定テーマの Button の見た目は効かない。
- `FocusScope` や `BackStack` は使わない。単独で完結する。

## World Space での置き方

World Space の panel でも、Screen Space と同じ構造のまま置ける。Toggle 側に World Space 用の設定は無い。
sandbox の Gallery の World Space の panel（1920×1080、`worldSpaceSizeMode = Fixed`）で、off と on が Screen Space と同じに写ることを確かめた（Unity 6000.7.0b4）。

## レイアウト

`width` は固定していない。`min-width` と内容で決まり、`flex-shrink: 0` なので横並びの親の中では縮まない。長い文字列を省略記号で切りたいときは、親を縦並び（`align-items: stretch`）にして幅を決める。
