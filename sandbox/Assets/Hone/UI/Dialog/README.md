# Dialog

開いている間フォーカスを中に閉じ込め、閉じると元の要素へフォーカスを戻し、Cancel で閉じるダイアログ。
フォーカスは `FocusScope` に、Cancel は `BackStack` に委ねていて、Dialog 自身はナビゲーションのイベントを扱わない。
自分で扱うのは overlay の PointerDown だけ。

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

Dialog は親の全面を覆う（`position: absolute` で上下左右 0）。panel 全体を覆うには root の直下の最後の子に置く。
UI Toolkit には z-index が無く、描画とポインタの判定は hierarchy の順なので、Dialog より後ろの兄弟は overlay の上に描かれ、入力も受ける。

## Open / Close

| 呼び出し | 動き |
|---|---|
| `Open()` | 表示し、呼んだ時点のフォーカスを記憶して、中の最初の focusable にフォーカスを当てる。`dismissOnCancel` なら `BackStack` に積む。`opened` を発火 |
| `Close()` | `BackStack` から外し、記憶した要素へフォーカスを戻し、隠す。`closed` を発火 |

- 開いている間の `Open()`、閉じている間の `Close()` は何もしない（イベントも発火しない）。
- `Open()` は panel に attach してから呼ぶ。attach 前に呼ぶと、表示の状態と、attach された時点での `BackStack` への積み込みだけが行われる。初期フォーカスは当たらず、戻す先も記憶しない。
- 開いたまま panel から外すと `BackStack` から外れる。開閉の状態（`isOpen`）は変わらず、付け直すと `BackStack` に積み直す。
- 戻す先が無いとき（`Open()` 前にフォーカスが無かった、記憶した要素が panel から外れた、または focusable でなくなった）は、`Close()` の後はどこにもフォーカスが無い状態になる（隠れた Dialog の中の要素からは外れる。6000.7.0b2 で確認）。
  続けてゲームパッドで操作させるなら、`closed` で利用者がフォーカスを当てる。

## 属性

| 属性 | 既定 | 意味 |
|---|---|---|
| `dismissOnOverlay` | `true` | overlay（背景）を押すと閉じる。`false` なら押しても閉じない（overlay は表示され、背後への入力は止める）。どちらでも PointerDown の既定のフォーカス処理は止めるので、閉じる時は `Close()` で戻したフォーカスが、閉じない時は Dialog の中のフォーカスが保たれる |
| `dismissOnCancel` | `true` | Cancel（ゲームパッドの B、Esc）で閉じる。`false` なら `BackStack` に積まない。`Open()` の時点の値で決まり、開いている間に変えても次に開くまで反映されない |

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
値はトークン `--hone-backdrop-blur`（`blur()` の完成形）で、`var()` 経由でも `resolvedStyle.backdropFilter` が `blur(8)` に解決されることを確認した（6000.7.0b2。描画結果は見ていない）。

## World Space での置き方

World Space では panel 自体が 1 つのダイアログの大きさになるので、Dialog は panel の root 直下に置く。Dialog は親（panel の root）の全面を覆うので、root の直下の最後の子にする。panel がダイアログだけなら、唯一の子になる。
World Space 用の設定や分岐は無く、Screen Space と同じ構造のまま動く。Unity 6000.7.0b2、Windows の Player で、マウスのポインタを `Camera` 経由で当てて確かめた。
scene には EventSystem（`InputSystemUIInputModule`）と、`processWorldSpaceInput = true` の `PanelInputConfiguration`（event camera は Main Camera）がある。

- overlay は、その Dialog がある panel の中だけを覆う。別の panel への入力は止めない。
  1 枚目（1920×1080）の Button と、2 枚目（480×300）の開いた Dialog の OK を続けてクリックすると、どちらの `clicked` も届いた。
- overlay の content の外側を押すと閉じ（`dismissOnOverlay = true`）、footer の OK のクリックは overlay の後ろに隠れず届く。
- content の幅は、panel の幅の 90% と `--hone-size-dialog`（512px）の小さい方。panel の幅が `--hone-size-dialog` 以下なら 90% で決まる。

  | panel の大きさ | content の幅 | 決まり方 |
  |---|---|---|
  | 1920×1080 | 512px | `--hone-size-dialog`（90% は 1728px） |
  | 480×300 | 432px | 90% |

  どちらも content の高さは 182px で、panel の中に収まった。
- 戻す先の記憶は、その Dialog がある panel の `focusController` だけを見る。別の panel の要素へはフォーカスを戻さない。
  1 枚目の panel の Button にフォーカスを当てて、2 枚目の panel の Dialog を `Open()` すると、Dialog の中の Cancel にフォーカスが移り、1 枚目の Button はフォーカスを失った。
  `Close()` の後は、どちらの panel にもフォーカスが無い（1 枚目の Button には戻らない）。
  別の panel のダイアログを閉じたあとに元の要素へフォーカスを戻すなら、`closed` で利用者が `Focus()` する。
- `hone-dialog--blur` は World Space では効かない（上の「USS クラス」のとおり）。

## headless 層との関係

- 中身の枠は `FocusScope`（`trap = true`、`autoFocus = false`）。方向入力と Tab は枠の中に留まる。
  枠は Dialog の `contentContainer` なので、中の要素の `parent`（論理上の親）は枠を飛ばして Dialog を指す。`FocusScope` は `hierarchy.parent` で祖先を辿るので、この構造でも留まる。
  `autoFocus` を `false` にしているのは、閉じた Dialog が panel に attach された時点でフォーカスを奪わないため。初期フォーカスは `Open()` が `FocusFirst()` で当てる。
- Cancel は `BackStack` が処理する。Dialog は `IDismissable` を実装し、`Dismiss()` で `Close()` する。重ねて開いたときは、最後に開いたものだけが閉じる。
- ボタン等の中身には何も要求しない。`Hone.Button` 以外の focusable でも同じように動く。registry の依存に `Button` があるのは、`Dialog.uxml` の使用例が使うため。
- 中に focusable が無いと初期フォーカスは当たらず、開く前のフォーカスが overlay の背後に残る（trap は外のフォーカスを中へ引き込まない）。
