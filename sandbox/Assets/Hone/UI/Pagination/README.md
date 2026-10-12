# Pagination

前後のページ送りと、ページ番号と省略記号の並びを持つ部品。shadcn の Pagination に当たる。
持つのは今のページ（`value`、0 始まり）とページ数（`pageCount`）だけで、ページごとの中身は持たない。ページが変わったら、利用者が `ChangeEvent<int>` を受けて中身を差し替える（`pageCount` を変えて `value` が収められたときは `ChangeEvent` が出ないので、`pageCount` を変えた後は `value` を読む）。
前後とページ番号は `Hone.Button` で、今のページは variant Outline、それ以外は Ghost。Pagination 自身は variant を持たない（前後とページ番号の Button の variant は固定）。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:hone="Hone">
    <hone:Pagination page-count="10" value="4" />
</ui:UXML>
```

C# で作り、ページの変化を受ける例:

```csharp
var pagination = new Hone.Pagination { pageCount = 10, value = 4 };

// ページが変わったら中身を差し替える
pagination.RegisterValueChangedCallback(evt => ShowPage(evt.newValue));

// ChangeEvent を出さずにページを変える
pagination.SetValueWithoutNotify(0);
```

C# で `pageCount` と `value` を一緒に設定するときは、`pageCount` を先に書く。`value` は `pageCount` の範囲に収められるので、逆だと既定の 1 ページに収められて 0 になる。
UXML の属性は、UXML に書いた順ではなく C# の宣言の順に適用される。`pageCount` を `value` より先に宣言してあるので、UXML ではどちらを先に書いても、`page-count="10"` と `value="4"` は 10 / 4 のまま残る（`value="4" page-count="10"` の順で書いても 10 / 4 になった。Unity 6000.7.0b4 で確認）。

## API

| 名前 | 意味 |
|---|---|
| `pageCount` | ページ数。既定 1。0 未満は 0 として扱う。変えると `value` を範囲に収めるが、`ChangeEvent` は出さない。同じ値を入れても作り直さない（フォーカスは保たれる） |
| `value` | 今のページ（0 始まり）。既定 0。`0`〜`max(pageCount - 1, 0)` に収め、変わったときだけ `ChangeEvent<int>` を 1 回出す |
| `SetValueWithoutNotify(int)` | `value` と同じに収めて並びを作り直すが、`ChangeEvent` を出さない |

| 押したもの | 結果 |
|---|---|
| 前の Button | `value` が 1 減る。最初のページでは disabled |
| 次の Button | `value` が 1 増える。最後のページでは disabled |
| ページ番号の Button | `value` がそのページになる。今のページを押しても `ChangeEvent` は出ない |

`pageCount` が 0 か 1 のときは、前後の Button が両方 disabled になる。0 のときはページ番号を出さない。

読み込み中などで送らせたくないときは、Pagination に `SetEnabled(false)` する。

## 表示するページ番号の規則

`pageCount` が 7 以下なら全ページを出す。8 以上なら常に 7 項目（省略記号を含む）を出すので、ページが変わっても前後の Button の位置はほぼ動かない（番号の文字幅の分だけ動く）。
`n = pageCount`、`c = value`（どちらも 0 始まり）として:

| 条件 | 並び（0 始まり。… は省略記号） |
|---|---|
| `n <= 7` | `0, 1, …, n-1`（省略記号なし） |
| `n >= 8` かつ `c <= 3` | `0, 1, 2, 3, 4, …, n-1` |
| `n >= 8` かつ `c >= n-4` | `0, …, n-5, n-4, n-3, n-2, n-1` |
| それ以外 | `0, …, c-1, c, c+1, …, n-1` |

`n = 10` の表示（1 始まり。`[ ]` が今のページ）:

| value | 表示 |
|---|---|
| 0 | `[1] 2 3 4 5 … 10` |
| 3 | `1 2 3 [4] 5 … 10` |
| 4 | `1 … 4 [5] 6 … 10` |
| 5 | `1 … 5 [6] 7 … 10` |
| 6 | `1 … 6 [7] 8 9 10` |
| 9 | `1 … 6 7 8 9 [10]` |

今のページの前後に何ページ出すか（今は 1）を変える属性は持たない。変えたいときは、コピーした `Pagination.cs` の `VisiblePages` を書き換える。分岐のしきい値（7、3、4、5）は「7 項目、両隣 1 つずつ」を前提にした組なので、一緒に変える。

## USS クラス

| クラス | 意味 |
|---|---|
| `hone-pagination` | ブロック。横並びで中央寄せ |
| `hone-pagination__previous` | 前の Button（`Hone.Button`、Ghost、"‹"） |
| `hone-pagination__items` | ページ番号と省略記号の入れ物。`value` か `pageCount` が変わるたびに中身を作り直す |
| `hone-pagination__item` | ページ番号の Button（`Hone.Button`）。今のページは Outline、それ以外は Ghost |
| `is-current` | 今のページの Button に付く。USS は書いていない。今のページの見た目を変える口 |
| `hone-pagination__ellipsis` | 省略記号（`Label`、"…"）。`hone-text` も付く |
| `hone-pagination__next` | 次の Button（`Hone.Button`、Ghost、"›"） |

ページ番号と前後の Button の見た目は `Button.uss` のまま。Pagination の USS では Button を上書きしない。

## 前後の記号の差し替え

前後の Button はコンストラクタで 1 回だけ作るので、取り出して `text` を変えれば残る。

```csharp
pagination.Q<Hone.Button>(className: "hone-pagination__previous").text = "Previous";
pagination.Q<Hone.Button>(className: "hone-pagination__next").text = "Next";
```

## headless 層との関係

- フォーカス ring は各 `Hone.Button` が持つ `hone-focusable`（`Core.uss`）のまま。Pagination 側では何も書かない。
- `FocusScope` や `BackStack` は使わない。単独で完結する。
- ページ番号の Button は作り直すので、今のページ以外の番号を押すとフォーカスが外れる（今のページの番号を押しても作り直さない）。作り直した後にフォーカスを戻す処理は持たない。
- 前後の Button は作り直さないので、途中のページで押してもフォーカスが残る。端のページに着くと押した Button 自身が disabled になる。そのときフォーカスがどこへ行くかは確かめていない。
- キーボードやゲームパッドの左右でページを送る処理は持たない。ナビゲーションは基底のまま。
- `Hone.Button` の `commitDelay` / `committed` は、ページの移動には関与しない。ページは Button の `clicked`（押した瞬間）で変わる。

## World Space での置き方

World Space の panel でも、Screen Space と同じ構造のまま置ける。Pagination 側に World Space 用の設定は無い。
幅は内容で決まる（Button は縮まない）。`pageCount` が 8 以上なら項目が常に 7 個なので、ページを送っても幅はほぼ変わらない（番号の文字幅の分だけ変わる）。panel が小さいときは、前後の Button を含めた 9 項目の幅が収まる大きさにする。
