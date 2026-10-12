# Switch

`UnityEngine.UIElements.Toggle` を継承した、on / off を切り替えるスイッチ。shadcn の Switch に当たる。
`value`、`ChangeEvent<bool>`、クリックと `NavigationSubmitEvent` での切り替え、`SetValueWithoutNotify` は基底のまま使える。
Switch 自身は振る舞いを足さず、基底が付けるクラスを付け替えるだけ。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:hone="Hone">
    <hone:Switch label="Airplane Mode" />
    <hone:Switch label="Notifications" value="true" />
</ui:UXML>
```

```csharp
var airplane = new Hone.Switch { label = "Airplane Mode" };
airplane.RegisterValueChangedCallback(evt => SetAirplaneMode(evt.newValue));
```

文言は `label` で渡す（左に出る）。`text` は使わない。基底の Toggle は `text` の Label を track の中に作るので、44px の track の中に文字が入る（Unity 6000.7.0b4 で確認）。

## 構造と USS クラス

| 要素 | USS クラス | 由来 |
|---|---|---|
| Switch（root） | `hone-switch` `hone-text` | 基底の `unity-toggle` を外して付ける。`unity-base-field` 系は残す。`:checked` `:focus` `:disabled` はここに付く |
| label の `Label` | `unity-base-field__label` | 基底のまま。`label` を設定したときだけ出る |
| track | `hone-switch__track` | 基底の visualInput（root の直接の子）。`unity-toggle__input` を外して付ける |
| thumb | `hone-switch__thumb` | 基底の checkmark（track の直接の子）。`unity-toggle__checkmark` を外して付ける |

| 状態 | 表示 |
|---|---|
| off | track は `--hone-color-border`、thumb は左 |
| on（`:checked`） | track は `--hone-color-primary`、thumb は右 |
| フォーカス（`:focus`） | track の枠が `--hone-color-ring` |
| disabled（`SetEnabled(false)`） | 全体が `opacity: 0.5` |

残している `unity-base-field` 系のクラスには既定テーマの規則が当たる。`resolvedStyle` で測って Switch の指定に勝っていた 3 つ（track の `flex-grow` と `flex-basis`、label の `min-width`、フォーカス時の label の色）は `Switch.uss` が上書きしている（Unity 6000.7.0b4）。root の左右の margin（`unity-base-field`）と label の padding は既定テーマのまま。

variant は持たない。thumb の移動はアニメーションしない（`transition` を書いていない）。動きは演出なので、利用者が `.hone-switch__thumb` に `transition` を付けるか、Manipulator で付ける。

## headless 層との関係

- `hone-focusable`（`Core.uss`）は付けない。root に付けると label を含む行全体が ring で囲まれ、shadcn の Switch（ring は track の周り）と違う見た目になる。ring は `Switch.uss` が track の border で描く（`.hone-switch:focus > .hone-switch__track`）。track の border は常に `--hone-ring-width` の太さで透明にしてあり、フォーカスで色だけ変わる。フォーカスで大きさは変わらない。
- `hone-text`（`Core.uss`）を root に付けてあり、label は本文フォントを継承する。
- `FocusScope` や `BackStack` は使わない。単独で完結する。

## World Space での置き方

World Space の panel でも、Screen Space と同じ構造のまま置ける。Switch 側に World Space 用の設定は無い。
sandbox の Gallery の World Space の panel（1920×1080、`worldSpaceSizeMode = Fixed`）で、off と on が Screen Space と同じに写ることを確かめた（Unity 6000.7.0b4）。

## レイアウト

label が `flex-grow: 1` で残りの幅を取り、track を右端へ押す。長い文言は省略記号で切れる。track は `flex-shrink: 0` で縮まない。
行の幅は親で決める（Switch 自身は幅を持たない）。
