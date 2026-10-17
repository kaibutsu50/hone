# Progress

`UnityEngine.UIElements.ProgressBar` を継承した、進捗率を出す細いバー。shadcn の Progress に当たる。
`value`、`lowValue`、`highValue`、`title`、`ChangeEvent<float>` は基底のまま使える。
Progress 自身は振る舞いを足さず、基底が付けるクラスを付け替えるだけ。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:hone="Hone">
    <hone:Progress value="60" />
</ui:UXML>
```

```csharp
var progress = new Hone.Progress();
// 読み込みが進むたびに、0〜100 の値を渡す
progress.value = loadedBytes * 100f / totalBytes;
```

`highValue` の既定は 100 なので、割合（0〜1）は 100 倍して渡すか、`highValue = 1` にする。
`UnityEditor` を `using` したファイルでは、`UnityEditor.Progress` と名前が重なるので `Hone.Progress` と完全修飾で書く。

## 構造と USS クラス

| 要素 | Progress が付ける USS クラス | 由来 |
|---|---|---|
| Progress（root） | `hone-progress` | 基底の `unity-progress-bar` を外して付ける |
| container | `hone-progress__container` | root の直接の子。`unity-progress-bar__container` を外して付ける |
| track | `hone-progress__track` | container の直接の子（基底の background）。`unity-progress-bar__background` を外して付ける |
| indicator | `hone-progress__indicator` | track の直接の子（基底の progress）。`unity-progress-bar__progress` を外して付ける |
| title の枠 | `hone-progress__title-container` | track の直接の子。`unity-progress-bar__title-container` を外して付ける |
| title の `Label` | `hone-progress__title` `hone-text` | title の枠の直接の子。`unity-progress-bar__title` を外して付ける（`unity-label` `unity-text-element` は残る） |

構造は Unity 6000.7.0b4 で確認したもの。`unity-progress-bar` 系のクラスを外すので、既定テーマの規則は当たらない。

| 要素 | 見た目 |
|---|---|
| track | 高さ `--hone-size-progress`（8px）、角丸 `--hone-radius-progress`（4px）、色 `--hone-color-progress-track`（`--hone-color-primary` の 20%） |
| indicator | 色 `--hone-color-primary`、角丸は track と同じ。左端から `value` の割合の幅まで |
| title | 表示しない（`display: none`） |

indicator の幅は基底が決める。`value` を設定した時点と track の大きさが変わった時点で、indicator の inline の `right` を px で書き換える。
`Progress.uss` は indicator を `position: absolute` で `left` `top` `bottom` を 0 にして、この `right` を効かせている。indicator に `width` や `right` を書かない。
基底は `right` を「(track の幅 - 2px) × (1 - 割合)」で決める（2px は既定テーマの track の左右の枠の分。Progress の track は枠を持たない）。
そのため 0 より大きい値では、indicator の幅は割合ちょうどより最大 2px 広い（track の幅 200px で `value` 25 なら 51.5px）。`value` が 0 のとき（`lowValue` と `highValue` は既定の 0 と 100）は、基底が幅を 0 にする（Unity 6000.7.0b4 で測った）。

variant は持たない。値の変化はアニメーションしない（`transition` を書いていない）。動きは演出なので、利用者が `.hone-progress__indicator` に `transition` を付ける。

## title

`title` は既定で表示しない（shadcn の Progress は文字を持たない）。文言は Progress の外に `Label` を置いて出す。
title を出したいときは、自分の USS で `.hone-progress__title-container` の `display` を `flex` にし、配置と文字の大きさを書く。
track の高さは 8px なので、文字を track の中に入れるなら `--hone-size-progress` も上書きする。

## レイアウト

`width` は固定していない。縦に並べた親の中では親の幅いっぱいに、横並びの親の中では `flex-grow` などで決まる。
`flex-shrink: 0` なので、縦に並べた親の高さが足りなくても、track の高さは縮まない。

## headless 層との関係

- `hone-focusable`（`Core.uss`）は付けない。Progress はフォーカスを受けない。
- `hone-text`（`Core.uss`）を title の `Label` に付けてあり、title を出したときは本文フォントに従う。
- `FocusScope` や `BackStack` は使わない。単独で完結する。
