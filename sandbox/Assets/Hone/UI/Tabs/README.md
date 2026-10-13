# Tabs

上に並べたタブで、下の中身を切り替える部品。shadcn の Tabs に当たる。
タブの列は `Hone.ToggleGroup`（単一選択、選択なしを許さない）で、タブは `Hone.Button`。選んだタブの中身だけを表示し、ほかを隠す。
持つのはタブのラベルの列（`tabs`）と選択中の index（`value`、0 始まり）だけ。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:hone="Hone">
    <hone:Tabs tabs="Account,Password">
        <ui:VisualElement>
            <ui:Label text="Make changes to your account here." class="hone-text" />
            <hone:Button text="Save changes" />
        </ui:VisualElement>
        <ui:VisualElement>
            <ui:Label text="Change your password here." class="hone-text" />
            <hone:Button text="Save password" />
        </ui:VisualElement>
    </hone:Tabs>
</ui:UXML>
```

UXML の子要素（C# では `Add()`）が中身になり、i 番目の子が i 番目のタブの中身になる。shadcn の `TabsList` `TabsTrigger` `TabsContent` に当たる別のクラスは無い。
タブの列は `tabs` から Tabs が作る。`tabs` は `Cycle` の `choices` と同じく、カンマ区切りで書く。

C# で作り、選択の変化を受ける例:

```csharp
var tabs = new Hone.Tabs { tabs = new List<string> { "Graphics", "Audio", "Controls" } };
tabs.Add(graphicsContent);
tabs.Add(audioContent);
tabs.Add(controlsContent);
root.Add(tabs);

// 中身の中の Cycle などが出す ChangeEvent<int> も Tabs まで上がってくるので、target で絞る
tabs.RegisterValueChangedCallback(evt =>
{
    if (evt.target == tabs)
        Debug.Log($"tab: {evt.newValue}");
});
```

C# で `tabs` と `value` を一緒に設定するときは、`tabs` を先に書く。`value` は `tabs` の範囲に収められる。
中身は panel に attach する前に足す。中身の表示は attach の時点と選択が変わった時点で揃えるので、attach 後に `Add()` した中身は、次に選択が変わるまで隠れない。

## API

| 名前 | 意味 |
|---|---|
| `tabs` | タブのラベル。既定は空。set のたびにタブの `Hone.Button` を作り直す。get は写しを返し、set は受け取った列を写して持つ。null は空として扱う。`value` を範囲に収めるが、`ChangeEvent` は出さない |
| `value` | 選択中のタブの index（0 始まり）。既定 0。`0`〜`max(tabs.Count - 1, 0)` に収め、変わったときだけ `ChangeEvent<int>` を 1 回出す |
| `SetValueWithoutNotify(int)` | `value` と同じに収めてタブの選択と中身の表示を揃えるが、`ChangeEvent` を出さない |

| 操作 | 結果 |
|---|---|
| タブを押す、またはタブにフォーカスして Submit | そのタブを選び、その中身だけを表示する。`ChangeEvent<int>` が 1 回出る |
| タブにフォーカスして左右 | 既定のナビゲーションで隣のタブへフォーカスが移る。選択は変わらない（Submit で選ぶ） |
| タブにフォーカスして Down | 表示中の中身の最初の focusable へ移る。中身に focusable が無ければ既定のナビゲーションのまま |
| 中身の最上段で Up | 選択中のタブへ移る（幾何で最も近いタブではない） |
| 中身の 2 段目以降で Up、それ以外の方向、Tab（Next / Previous） | 消費しない。既定のナビゲーションのまま |

「最上段」は、表示中の中身の focusable のうち、中心が今の要素の上端より上にあるものが無いこと。
中身の数がタブの数より多いとき、余った中身は表示されない。少ないときは、中身の無いタブを選ぶと何も表示されない。

## USS クラス

| クラス | 意味 |
|---|---|
| `hone-tabs` | ブロック。縦並び |
| `hone-tabs__list` | タブの列（`Hone.ToggleGroup`。`hone-toggle-group` も付く）。地は `--hone-color-muted`、角丸は `--hone-radius-lg` |
| `.hone-tabs .hone-tabs__list .hone-button` | タブ。選ばれていないタブは地が透明で、文字は `--hone-color-muted-foreground` |
| `.hone-tabs .hone-tabs__list .hone-button:checked` | 選ばれているタブ。地は `--hone-color-background`。フォーカスが中身にあっても残る |
| `hone-tabs__panels` | 中身の入れ物（`contentContainer`）。タブの列との間に `--hone-space-2` |
| `hone-tabs__content` | 各中身。Tabs が付ける |
| `hone-tabs__content.is-active` | 表示中の中身。Tabs が付け外しする |

中身の表示の切り替えは、Tabs が inline の `display`（`flex` / `none`）で行う（Dialog と同じく、USS が読まれていなくても隠れる）。

## 演出の口

Tabs は切り替えの演出（スライド、フェード）を持たない。演出を足すときは、次を口にする。

- 選択の変化: `ChangeEvent<int>`（`target` が Tabs のもの）
- 選ばれているタブ: `:checked`
- 表示中の中身: `.is-active`

## headless 層との関係

- root が `NavigationMoveEvent` を TrickleDown で受け、上の表の Down と Up のときだけ `FocusController.IgnoreEvent` で既定の移動を止めてから `Focus()` する。フォーカスの変更は非同期で、同じ frame では確定しない。
- タブの列が中身の上にある前提で、Down と Up だけを扱う。タブの列を横に置く（縦のタブ）ときは、コピーしたコードの方向を書き換える。
- タブにフォーカスがあるとき、`focusController.focusedElement` はタブの `Hone.Button` ではなく、タブの列の ToggleGroup を返す（`ToggleButtonGroup` が composite root のため。Unity 6000.7.0b4 で確認）。どのタブかを知りたいときは、各タブの `FocusEvent` で受ける。
- `FocusScope` と `BackStack` は使わない。Cancel は基底のまま上へ届く。
- `FocusScope { trap = true }` の中（Dialog の中など）に置いたときの動作は確かめていない。FocusScope も同じ `NavigationMoveEvent` を Tabs より先に受けて自分で `Focus()` するので、Down と Up の行き先が食い違う可能性がある。

## World Space での置き方

World Space の panel でも、Screen Space と同じ構造のまま置ける。Tabs 側に World Space 用の設定は無い。
幅は親に従い、タブは `flex-grow: 1` で余った幅を等分して広がる（ToggleGroup と同じ）。長いラベルは省略記号で切れる。
