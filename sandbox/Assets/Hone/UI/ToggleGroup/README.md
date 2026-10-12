# ToggleGroup

`UnityEngine.UIElements.ToggleButtonGroup` を継承した、ボタンを並べて単一選択または複数選択にする部品。shadcn の Toggle Group（default variant）に当たる。
選択の状態（`value`、`ToggleButtonGroupState`）、`ChangeEvent<ToggleButtonGroupState>`、`SetValueWithoutNotify`、`isMultipleSelection`、`allowEmptySelection` は基底のまま使い、ToggleGroup はクラスの付け替えと USS だけを持つ。
子は `Hone.Button`。選ばれている子には基底が `:checked` を付け、USS がそれを accent の背景にする。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:hone="Hone">
    <hone:ToggleGroup>
        <hone:Button text="Day" />
        <hone:Button text="Week" />
        <hone:Button text="Month" />
    </hone:ToggleGroup>
</ui:UXML>
```

選択を C# から設定し、変化を受ける例（`ToggleButtonGroupState` の `length` は子の数ではなく常に 64。全部 false にしてから 1 つを true にする）:

```csharp
var group = new Hone.ToggleGroup();
group.Add(new Hone.Button { text = "Day" });
group.Add(new Hone.Button { text = "Week" });
group.Add(new Hone.Button { text = "Month" });

// 選択を設定する。ChangeEvent は出さない（出したいときは group.value = state）
var state = group.value;
for (var i = 0; i < state.length; i++)
    state[i] = false;
state[1] = true;
group.SetValueWithoutNotify(state);

// 選択の変化を受ける
group.RegisterValueChangedCallback(evt =>
{
    for (var i = 0; i < evt.newValue.length; i++)
    {
        if (evt.newValue[i])
            Debug.Log($"selected: {i}");
    }
});
```

## 基底のまま使うもの

| 名前 | 意味 |
|---|---|
| `value` | 選択の状態（`ToggleButtonGroupState`）。変えると `ChangeEvent<ToggleButtonGroupState>` が 1 回出る |
| `SetValueWithoutNotify(state)` | `ChangeEvent` を出さずに選択を設定する |
| `isMultipleSelection` | 複数選択にする。既定 false（単一選択） |
| `allowEmptySelection` | 何も選ばれていない状態を許す。既定 false |

- 単一選択は「押した項目が選ばれる」、複数選択は「押すたびにその項目が反転する」。クリックも、フォーカス時の `NavigationSubmitEvent` も同じ。
- ToggleGroup 自身は variant や属性を持たない。

## USS クラス

| クラス | 意味 |
|---|---|
| `hone-toggle-group` | ブロック。`unity-toggle-button-group` は外してある |
| `.hone-toggle-group .hone-button` | 子の Button。off は背景が透明で、hover で `--hone-color-muted`。`flex-grow: 1` で均等に広がり、親が狭いときは縮んで省略記号になる |
| `.hone-toggle-group .hone-button:checked` | 選ばれている子。背景は `--hone-color-accent`（選ばれたまま hover しても accent のまま） |
| `:disabled`（`SetEnabled(false)`） | `opacity: 0.5`（`Button.uss`） |

基底が付けるクラスは外していない。root には `unity-base-field`（と、label が無いときの `unity-base-field--no-label`）、子の Button の親のコンテナには `unity-base-field__input` と `unity-button-group`（`ToggleButtonGroup.buttonGroupClassName`）、子の Button には `unity-button-group__button` などが付く（Unity 6000.7.0b4 で確認）。子の Button の間に隙間は無い。

## 縦並び（選択状態を持つリストの行）

コンテナを `flex-direction: column` にするだけで縦並びになる。ToggleGroup に縦並びの属性は無いので、利用者が USS に書く。行は Button の中に子要素を入れて組む。

```css
.settings-list > .unity-button-group {
    flex-direction: column;
}

.settings-list .hone-button {
    -unity-text-align: middle-left;
}
```

```xml
<hone:ToggleGroup class="settings-list">
    <hone:Button text="Graphics" />
    <hone:Button text="Audio" />
    <hone:Button text="Controls" />
</hone:ToggleGroup>
```

## 子に Toggle は使わない

子には `Hone.Button` を使い、`Hone.Toggle` は使わない。Toggle 自身の on / off（`.is-pressed`）と、基底が付ける `:checked` の選択が二重になり、見た目と値が食い違う。
Toggle は単独で on / off を持つボタン（ミュート、表示の切り替えなど）に使う。

## headless 層との関係

- フォーカス ring は子の `Hone.Button` が持つ `hone-focusable`（`Core.uss`）のまま。ToggleGroup 側では何も書かない。
- `FocusScope` や `BackStack` は使わない。単独で完結する。
- `Hone.Button` の `commitDelay` / `committed` は、ToggleGroup の選択には関与しない。選択の確定は基底の `clicked` 経由で、押した瞬間に変わる。

## 名前の衝突

`UnityEngine.UIElements` にも `Button` がある。子を C# で足すファイルで `using UnityEngine.UIElements;` と Hone の `Button` を併用するときは、エイリアスで解く（`Button` の README の「名前の衝突」と同じ）。

```csharp
using UnityEngine.UIElements;
using Button = Hone.Button;
```

## World Space での置き方

World Space の panel でも、Screen Space と同じ構造のまま置ける。ToggleGroup 側に World Space 用の設定は無い。

## レイアウト

幅は親に従い、項目は `flex-grow: 1` で均等に広がる。ToggleGroup 自身は幅を固定しない。
