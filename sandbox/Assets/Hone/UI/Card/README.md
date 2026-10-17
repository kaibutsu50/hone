# Card

見出し・説明・中身・フッターを縦に並べる、枠付きのパネル。`.cs` を持たない見た目だけの部品で、USS クラスを `VisualElement` に付けて使う。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:VisualElement class="hone-card">
        <ui:VisualElement class="hone-card__header">
            <ui:Label text="Create project" class="hone-card__title hone-text" />
            <ui:Label text="Deploy your new project in one click." class="hone-card__description hone-text" />
        </ui:VisualElement>
        <ui:VisualElement class="hone-card__content">
            <ui:Label text="Your project is created in the current workspace." class="hone-text" />
        </ui:VisualElement>
        <ui:VisualElement class="hone-card__footer">
            <ui:Label text="Footer" class="hone-text" />
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>
```

## USS クラス

| USS クラス | 役割 |
|---|---|
| `hone-card` | root。背景・枠・角丸 |
| `hone-card__header` | 見出しと説明を縦に並べる枠。上下左右に余白 |
| `hone-card__title` | 見出し。`--hone-font-size-2xl` |
| `hone-card__description` | 説明。`--hone-font-size-sm`、`--hone-color-muted-foreground` |
| `hone-card__content` | 中身。上の余白は持たない |
| `hone-card__footer` | フッター。子を横に並べる。上の余白は持たない |

header・content・footer はどれも省いてよい。ただし content と footer は上の余白を持たない（header の下の余白で間が空く）ので、header を省くときは、先頭になる要素に上の余白を自分で足す。文字を持つ要素（title、description、中身の Label）には `hone-text` を付けて、本文フォントに従わせる。

## variant

| variant | USS クラス | 用途 |
|---|---|---|
| Default | `hone-card` のみ | 余白が広い、見出しが大きいパネル |
| Compact | `hone-card--compact` | 余白を `--hone-space-4` に詰め、見出しを `--hone-font-size-xl` にしたパネル |

## レイアウト

`width` は固定していない。縦に並べた親の中では親の幅いっぱいに、横並びの親の中では `flex-grow` などで決まる。panel 全面や中央への配置もしない。
title、description、content の中の Label は、幅に収まらなければ折り返す（既定の Label は折り返さないので、`white-space: normal` を当てている）。footer の Label は折り返さない。
影は付けない（shadcn の `shadow-sm` に当たるトークンが無い）。

## headless 層との関係

振る舞いは持たない。Core は `.hone-text` などの USS だけを使う。
