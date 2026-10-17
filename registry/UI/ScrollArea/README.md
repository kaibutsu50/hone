# ScrollArea

`ui:ScrollView` の縦のスクロールバーを、トークンで整える。`.cs` を持たない見た目だけの部品で、USS クラス `hone-scroll-area` を `ScrollView` に付けて使う。スクロール・つまみのドラッグ・ホイールは、`ScrollView` の標準の振る舞いのまま。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:VisualElement style="height: 96px;">
        <ui:ScrollView class="hone-scroll-area" mode="Vertical" horizontal-scroller-visibility="Hidden" style="flex-grow: 1;">
            <ui:Label text="Terms of use" class="hone-text" style="white-space: normal;" />
            <ui:Label text="Hone is a set of UI components for Unity UI Toolkit. Copy the components into your project and change them freely." class="hone-text" style="white-space: normal;" />
            <ui:Label text="Licenses" class="hone-text" style="white-space: normal;" />
            <ui:Label text="Each component keeps its own behavior. Only the look is yours to decide, through the tokens in Tokens.uss." class="hone-text" style="white-space: normal;" />
            <ui:Label text="Notices" class="hone-text" style="white-space: normal;" />
            <ui:Label text="The vertical scrollbar appears only when the content does not fit in the height of the ScrollView." class="hone-text" style="white-space: normal;" />
        </ui:ScrollView>
    </ui:VisualElement>
</ui:UXML>
```

## 見た目

| 要素 | 見た目 |
|---|---|
| 縦のスクロールバー | 幅 `--hone-size-scrollbar`（10px）。背景は透明で、枠は無い |
| 上下の矢印ボタン | 表示しない |
| つまみ | 色 `--hone-color-border`、角丸 `--hone-radius-scrollbar`（5px）。幅はスクロールバーいっぱい |

variant は無い。整えるのは縦のスクロールバーだけで、横のスクロールバーは整えない（使用例のように `horizontal-scroller-visibility="Hidden"` で隠す）。

World Space でレイ（ポインタ）で掴みやすくしたいときは、`--hone-size-scrollbar` と `--hone-radius-scrollbar` を `HoneTheme.tss` の `:root` で上書きする。つまみの端を半円にするため、`--hone-radius-scrollbar` は `--hone-size-scrollbar` の半分に揃える。

## レイアウト

`hone-scroll-area` は padding も高さも持たない。高さは親が決める。縦並びの親（既定の `flex-direction: column`）の中では、`flex-grow: 1` を付けると親の残りの高さを使い、中身がそれより高いときにスクロールバーが出る。使用例の `style="height: 96px;"` は親の高さを固定した例。
中身が `ScrollView` の高さに収まるときは、`vertical-scroller-visibility` の既定（Auto）でスクロールバーが消える。常に出したいときは `Visible` にする。
中身の Label は、幅に収まらなければ折り返すよう `white-space: normal;` を付ける（既定の Label は折り返さない）。

## headless 層との関係

振る舞いは持たない。Core は `.hone-text` などの USS だけを使う（ScrollArea 自身は文字を持たないので、`hone-text` は使用例の Label のため）。フォーカスやナビゲーション（上下キー・スティック）でスクロールする処理も持たない。
