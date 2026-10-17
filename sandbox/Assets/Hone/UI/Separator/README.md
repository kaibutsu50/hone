# Separator

要素の間に引く、`--hone-border-width` の太さの線。`.cs` を持たない見た目だけの部品で、USS クラスを `VisualElement` に付けて使う。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:VisualElement>
        <ui:Label text="Hone" class="hone-text" />
        <ui:VisualElement class="hone-separator" />
        <ui:Label text="UI components for Unity UI Toolkit." class="hone-text" />
        <ui:VisualElement style="flex-direction: row;">
            <ui:Label text="Blog" class="hone-text" style="margin-left: 12px; margin-right: 12px;" />
            <ui:VisualElement class="hone-separator hone-separator--vertical" />
            <ui:Label text="Docs" class="hone-text" style="margin-left: 12px; margin-right: 12px;" />
            <ui:VisualElement class="hone-separator hone-separator--vertical" />
            <ui:Label text="Source" class="hone-text" style="margin-left: 12px; margin-right: 12px;" />
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>
```

## variant

| variant | USS クラス | 用途 |
|---|---|---|
| Horizontal | `hone-separator` のみ | 横線。高さが `--hone-border-width` |
| Vertical | `hone-separator hone-separator--vertical` | 縦線。幅が `--hone-border-width` |

どちらも色は `--hone-color-border`。縦線は `hone-separator` と `hone-separator--vertical` の両方を付ける。

## レイアウト

横線は縦並びの親（既定の `flex-direction: column`）の中で、縦線は横並びの親（`flex-direction: row`）の中で、親の幅（高さ）いっぱいに伸びる。向きが逆の親の中に置くと、線の長さが 0 になる（伸ばしたいときは、`flex-grow: 1` か `width` / `height` を自分で付ける）。
`flex-shrink: 0` なので、並びの方向に親の大きさが足りなくても（横線は親の高さ、縦線は親の幅）、線の太さは縮まない。
線の両側の余白は持たない。使用例の Label のように、隣の要素に `margin` を付ける。
文字を挟んだ区切り（「または」付きの線）は持たない。

## headless 層との関係

振る舞いは持たない。Core は `.hone-text` などの USS だけを使う（Separator 自身は文字を持たないので、`hone-text` は使用例の Label のため）。
