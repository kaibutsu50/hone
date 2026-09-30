# Dialog

開いている間フォーカスを中に閉じ込め、閉じると元の要素へフォーカスを戻し、Cancel で閉じるダイアログ。
フォーカスは `FocusScope` に、Cancel は `BackStack` に委ねていて、Dialog 自身はナビゲーションのイベントを扱わない。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:hone="Hone">
    <hone:Dialog name="dialog">
        <ui:Label class="hone-dialog__title hone-text" text="Delete this item?" />
        <ui:Label class="hone-dialog__description hone-text" text="This action cannot be undone." />
        <ui:VisualElement class="hone-dialog__footer">
            <hone:Button name="cancel" variant="outline" text="Cancel" />
            <hone:Button name="ok" text="OK" />
        </ui:VisualElement>
    </hone:Dialog>
</ui:UXML>
```

```csharp
var dialog = root.Q<Hone.Dialog>("dialog");
dialog.Q<Hone.Button>("cancel").clicked += dialog.Close;
dialog.closed += OnDialogClosed;
dialog.Open();
```

Dialog は親の全面を覆う（`position: absolute` で上下左右 0）。panel 全体を覆うには root の直下に置く。

## Open / Close

| 呼び出し | 動き |
|---|---|
| `Open()` | 表示し、呼んだ時点のフォーカスを記憶して、中の最初の focusable にフォーカスを当てる。`dismissOnCancel` なら `BackStack` に積む。`opened` を発火 |
| `Close()` | `BackStack` から外し、記憶した要素へフォーカスを戻し、隠す。`closed` を発火 |

- 開いている間の `Open()`、閉じている間の `Close()` は何もしない（イベントも発火しない）。
- `Open()` は panel に attach してから呼ぶ。attach 前に呼ぶと表示の状態だけ開き、`BackStack` に積まれないので Cancel では閉じない。
- 開いたまま panel から外すと `BackStack` から外れる。開閉の状態（`isOpen`）は変わらない。
- `Open()` 前にフォーカスが無かったときは、`Close()` で戻す先が無く、フォーカスは隠れた Dialog の中に残る。

## 属性

| 属性 | 既定 | 意味 |
|---|---|---|
| `modal` | `true` | overlay（背景）を押すと閉じる。`false` なら押しても閉じない（overlay は表示され、背後への入力は止める） |
| `dismissOnCancel` | `true` | Cancel（ゲームパッドの B、Esc）で閉じる。`false` なら `BackStack` に積まない |

## USS クラス

| クラス | 付く要素 |
|---|---|
| `hone-dialog` | Dialog 自身 |
| `hone-dialog__overlay` | 背景。Dialog が作る |
| `hone-dialog__content` | 中身の枠（`FocusScope`）。UXML の子要素と `Add()` はここに入る |
| `hone-dialog__title` `hone-dialog__description` | タイトルと本文の `Label` に利用者が付ける。折り返す |
| `hone-dialog__footer` | ボタンの行に利用者が付ける。右寄せで、幅が足りなければ折り返す |
| `hone-dialog--blur` | opt-in。overlay の背後をぼかす（`--hone-backdrop-blur`） |

`hone-dialog--blur` は `backdrop-filter` を使う。URP のみ、Screen Space のみで、World Space では効かない。

## headless 層との関係

- 中身の枠は `FocusScope`（`trap = true`、`autoFocus = false`）。方向入力と Tab は枠の中に留まる。
  `autoFocus` を `false` にしているのは、閉じた Dialog が panel に attach された時点でフォーカスを奪わないため。初期フォーカスは `Open()` が `FocusFirst()` で当てる。
- Cancel は `BackStack` が処理する。Dialog は `IDismissable` を実装し、`Dismiss()` で `Close()` する。重ねて開いたときは、最後に開いたものだけが閉じる。
- ボタン等の中身には何も要求しない。`Hone.Button` 以外の focusable でも同じように動く。
