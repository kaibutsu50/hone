# ListDetailScreen

「左: 1 ページ数件の選択リストとページ送り / 右: 選んだ項目の詳細と決定の Button」の画面の block。プリミティブ（ToggleGroup / Button / Pagination）と見た目だけの Card の合成だけで、C# は持たない。
読み込み中と空の表示（Label）も持つ。項目名・詳細の行・決定の文言は雛形なので、コピーした後に自分のファイルで書き換える。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:hone="Hone">
    <ui:VisualElement class="hone-list-detail-screen">
        <ui:Label class="hone-list-detail-screen__title hone-text" text="Items" />
        <ui:VisualElement class="hone-list-detail-screen__body">
            <ui:VisualElement class="hone-list-detail-screen__list">
                <ui:VisualElement class="hone-list-detail-screen__list-area">
                    <hone:ToggleGroup name="items" class="hone-list-detail-screen__items" allow-empty-selection="true">
                        <!-- 行は C# で、ページを出すたびに作り直す（下の「配線」）。ListDetailScreen.uxml には 5 行の使用例がある -->
                    </hone:ToggleGroup>
                    <ui:Label name="loading" class="hone-list-detail-screen__loading hone-text" text="Loading…" />
                    <ui:Label name="empty" class="hone-list-detail-screen__empty hone-text" text="No items" />
                </ui:VisualElement>
                <hone:Pagination name="pagination" class="hone-list-detail-screen__pagination" />
            </ui:VisualElement>
            <ui:VisualElement class="hone-card hone-card--compact hone-list-detail-screen__detail">
                <ui:VisualElement class="hone-card__header">
                    <ui:Label name="detail-title" class="hone-card__title hone-text" />
                    <ui:Label name="detail-description" class="hone-card__description hone-text" />
                </ui:VisualElement>
                <ui:VisualElement name="detail-fields" class="hone-card__content">
                    <ui:VisualElement class="hone-list-detail-screen__field">
                        <ui:Label class="hone-list-detail-screen__field-label hone-text" text="Category" />
                        <ui:Label class="hone-list-detail-screen__field-value hone-text" />
                    </ui:VisualElement>
                    <!-- Size / Updated の行は ListDetailScreen.uxml を参照 -->
                </ui:VisualElement>
                <ui:VisualElement class="hone-card__footer hone-list-detail-screen__actions">
                    <hone:Button name="confirm" text="Start" />
                </ui:VisualElement>
            </ui:VisualElement>
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>
```

`ListDetailScreen.uxml` は、5 行のページの 1 行目が選ばれ、詳細にその項目が出ている状態の使用例。`items` の `value="00001"` は右端の文字が 1 行目（index 0）に当たる（Unity 6000.7.0b4 で確認）。

## 配線

block は振る舞いを持たない。全件のデータ、1 ページの件数、選んだ項目（全件の中の index。未選択は -1）は利用者のコードが持ち、次のようにつなぐ。

```csharp
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Button = Hone.Button;

class Item
{
    public string Name;
    public string Meta;
    public string Description;
    public string[] Values;  // 詳細の __field の順。数は __field の行の数と同じにする
}

class ListDetailScreenWiring
{
    const int PageSize = 5;

    readonly VisualElement m_Root;
    readonly Hone.ToggleGroup m_Items;
    readonly Hone.Pagination m_Pagination;
    readonly Button m_Confirm;
    IReadOnlyList<Item> m_Data = Array.Empty<Item>();
    int m_Selected = -1;

    // root は .hone-list-detail-screen の要素
    public ListDetailScreenWiring(VisualElement root, Action<Item> onConfirm)
    {
        m_Root = root;
        m_Items = root.Q<Hone.ToggleGroup>("items");
        m_Pagination = root.Q<Hone.Pagination>("pagination");
        m_Confirm = root.Q<Button>("confirm");

        m_Pagination.RegisterValueChangedCallback(evt => ShowPage(evt.newValue));
        m_Items.RegisterValueChangedCallback(evt =>
        {
            // true の行があればその項目を選ぶ。無ければ（選んだ行をもう一度押して外れた）未選択
            m_Selected = -1;
            for (var i = 0; i < Math.Min(m_Items.childCount, evt.newValue.length); i++)
            {
                if (evt.newValue[i])
                {
                    m_Selected = m_Pagination.value * PageSize + i;
                    break;
                }
            }
            ShowDetail();
        });
        m_Confirm.clicked += () => onConfirm(m_Data[m_Selected]);
    }

    public void SetLoading()
    {
        m_Root.RemoveFromClassList("is-empty");
        m_Root.AddToClassList("is-loading");
        m_Pagination.SetEnabled(false);
        m_Selected = -1;
        ShowDetail();
    }

    public void SetItems(IReadOnlyList<Item> data)
    {
        m_Data = data;
        m_Selected = -1;
        m_Root.RemoveFromClassList("is-loading");
        m_Root.EnableInClassList("is-empty", data.Count == 0);
        m_Pagination.SetEnabled(true);
        // pageCount を先に、value を後に書く（Pagination の README）。0 件なら 0 ページ
        m_Pagination.pageCount = (data.Count + PageSize - 1) / PageSize;
        m_Pagination.SetValueWithoutNotify(0);
        ShowPage(0);
        ShowDetail();
    }

    void ShowPage(int page)
    {
        // 作り直しの途中で基底が ChangeEvent を出さないよう、外す前と足す前に、全部 false で length 64 の value にする（下の注記）
        var none = new ToggleButtonGroupState(0, 64);
        m_Items.SetValueWithoutNotify(none);
        m_Items.Clear();
        m_Items.SetValueWithoutNotify(none);
        var start = page * PageSize;
        var end = Math.Min(start + PageSize, m_Data.Count);
        for (var i = start; i < end; i++)
        {
            var row = new Button();
            row.AddToClassList("hone-list-detail-screen__item");
            row.Add(CreateLabel("hone-list-detail-screen__item-label", m_Data[i].Name));
            row.Add(CreateLabel("hone-list-detail-screen__item-meta", m_Data[i].Meta));
            m_Items.Add(row);
        }

        // 足し終えてから、選んだ項目がこのページにあればその行だけを選ぶ
        var state = none;
        if (m_Selected >= start && m_Selected < end)
            state[m_Selected - start] = true;
        m_Items.SetValueWithoutNotify(state);
    }

    void ShowDetail()
    {
        var fields = m_Root.Q("detail-fields");
        if (m_Selected < 0)
        {
            m_Root.Q<Label>("detail-title").text = "Select an item";
            m_Root.Q<Label>("detail-description").text = string.Empty;
            fields.style.display = DisplayStyle.None;
            m_Confirm.SetEnabled(false);
            return;
        }

        var item = m_Data[m_Selected];
        m_Root.Q<Label>("detail-title").text = item.Name;
        m_Root.Q<Label>("detail-description").text = item.Description;
        var values = fields.Query<Label>(className: "hone-list-detail-screen__field-value").ToList();
        for (var i = 0; i < values.Count; i++)
            values[i].text = item.Values[i];
        fields.style.display = DisplayStyle.Flex;
        m_Confirm.SetEnabled(true);
    }

    static Label CreateLabel(string className, string text)
    {
        var label = new Label(text);
        label.AddToClassList(className);
        label.AddToClassList("hone-text");
        return label;
    }
}
```

- コンストラクタの直後に `SetLoading()` か `SetItems(data)` を呼ぶ（呼ぶまでは UXML の使用例の行と詳細が残っていて、配線と合わない）。読み込みを始めたら `SetLoading()`、読み終えたら `SetItems(data)` を呼ぶ。`is-loading` と `is-empty` は同時に付けない。
- 行の子に `Hone.Toggle` は使わない（ToggleGroup の README の「子に Toggle は使わない」）。
- `items` の `allow-empty-selection="true"` は外さない。false だと、最初の子を足した時点で基底が index 0 を選び、選んだ項目が無いページでも 1 行目が選ばれる。
- 行を作り直すとき、選ばれている行があるまま外すと、基底が残りの子の数の length で `ChangeEvent` を出すことがある。`Clear()` で length が 0 になり、そのまま子を足すと、足すたびに length を伸ばして `ChangeEvent`（全部 false）を出す。Pagination の `ChangeEvent<int>` の中で作り直すと、それらが作り直しの後に届き、選んだ項目が外れる（Unity 6000.7.0b4 で確認）。対処は、`Clear()` の前と、子を足す前に、`SetValueWithoutNotify(new ToggleButtonGroupState(0, 64))` で全部 false・length 64 の value にすること。
- ページを送ると行を作り直すので、行にあったフォーカスは外れる。選択と詳細は変わらず、選んだ項目のあるページに戻ると、その行が選ばれた状態で出る。
- 読み込み中と空の Label は、C# の `style.display` で切り替えない。root の `is-loading` / `is-empty` で USS が切り替える（見た目を USS で変えられるように）。
- 失敗の表示は持たない。出すときは、`__empty` と同じ場所（`__list-area` の中）に Label を足し、root のクラスで切り替える。

## USS クラス

| クラス | 付く要素 |
|---|---|
| `hone-list-detail-screen` | 画面全体。縦並びで、親の残りの高さを取る |
| `hone-list-detail-screen__title` | 画面のタイトルの `Label`。折り返す |
| `hone-list-detail-screen__body` | 一覧の列と詳細の横並び |
| `hone-list-detail-screen__list` | 左の列（一覧と Pagination）。`__body` の 40% の幅 |
| `hone-list-detail-screen__list-area` | 一覧の領域。残りの高さを取り、行が少ないページでも Pagination の位置を変えない |
| `hone-list-detail-screen__items` | 一覧の `Hone.ToggleGroup`。縦並び |
| `hone-list-detail-screen__item` | 1 行（`Hone.Button`）。項目名と補足が横に並ぶ。縦に伸びない |
| `hone-list-detail-screen__item-label` | 行の項目名の `Label`。残りの幅を取り、収まらなければ省略記号 |
| `hone-list-detail-screen__item-meta` | 行の補足（件数など）の `Label`。縮まない |
| `hone-list-detail-screen__loading` `hone-list-detail-screen__empty` | 読み込み中と空の `Label`。既定は表示しない。一覧の領域の中央に出る |
| `hone-list-detail-screen__pagination` | 一覧の下の `Hone.Pagination` |
| `hone-list-detail-screen__detail` | 右の詳細（`hone-card hone-card--compact`）。残りの幅を取って縮む |
| `hone-list-detail-screen__field` | 詳細の 1 行（項目名と値の横並び） |
| `hone-list-detail-screen__field-label` `hone-list-detail-screen__field-value` | 詳細の行の項目名（残りの幅を取る）と値（右寄せで縮まない） |
| `hone-list-detail-screen__actions` | 詳細の footer。決定の Button を右に寄せる |
| `is-loading` | root に付ける。行を隠し、`__loading` を出す |
| `is-empty` | root に付ける。行を隠し、`__empty` を出す |

## レイアウトの規約

- 画面は親の残りの高さを取る（`flex-grow: 1`）。行が少ないページで Pagination の位置が動かないのは、親の高さが決まっていて（panel の root など）、一覧の領域が余りの高さを取れるときだけ。親の高さが内容で決まると、行の数に応じて動く。
- 列の幅は画面の割合で決める。固定幅を書かない（言語によって文字列の幅が変わる）。一覧の列（40%）には、Pagination の前後とページ番号の Button（縮まない）が収まる幅が要る。`pageCount` が 3 なら 5 つ、8 以上なら 9 つ（Pagination の README）で、収まらないと Pagination が列からはみ出す。
- 行の項目名は 1 行で、収まらなければ省略記号になる。詳細の見出し・説明・行の項目名と値は折り返す（Card の USS）。
- 画面の背景色・固定幅・中央寄せは持たない。World Space では panel 自体が画面の大きさになる。Screen Space で中央に置きたいときは、利用者が親に書く。
- 選んだ行の見た目は ToggleGroup の既定（`:checked` で accent の背景）のまま。

## headless 層との関係

block は `FocusScope` も `BackStack` も直接使わない。フォーカスの移動は各プリミティブ（ToggleGroup の行の Button、Pagination、決定の Button）の既定の動きのまま。

registryDependencies の `Core`、`Button`、`ToggleGroup`、`Pagination`、`Card` は、`ListDetailScreen.uxml` が使うもの。
