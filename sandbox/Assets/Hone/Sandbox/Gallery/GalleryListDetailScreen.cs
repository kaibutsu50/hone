using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Button = Hone.Button;

namespace Hone.Sandbox.Gallery
{
    // ListDetailScreen block の 1 件分のデータ（block の README の Item と同じ形）。詳細の見出しは name と同じ。values は詳細の項目の行（UXML の __field）の順の値で、数は行の数と同じにする
    public readonly struct ListDetailItem
    {
        public readonly string Name;
        public readonly string Meta;
        public readonly string Description;
        public readonly IReadOnlyList<string> Values;

        public ListDetailItem(string name, string meta, string description, IReadOnlyList<string> values)
        {
            Name = name;
            Meta = meta;
            Description = description;
            Values = values;
        }
    }

    // ListDetailScreen block の配線（block の README の「配線」に、SetItems の初期選択の引数を足したもの）。block は振る舞いを持たないので、Gallery はここで配線する。
    // GalleryHoneEntries が block の root の userData に入れる。テストと撮影の eval は、そこから SetItems / SetLoading を呼び、items / selectedIndex を読む
    public sealed class GalleryListDetailScreen
    {
        public const int PageSize = 5;
        const string SelectPrompt = "Select an item";

        readonly VisualElement m_Root;
        readonly ToggleGroup m_Items;
        readonly Pagination m_Pagination;
        readonly Label m_DetailTitle;
        readonly Label m_DetailDescription;
        readonly VisualElement m_DetailFields;
        readonly Button m_Confirm;
        IReadOnlyList<ListDetailItem> m_Data = Array.Empty<ListDetailItem>();
        int m_Selected = -1;

        // root は .hone-list-detail-screen の要素
        public GalleryListDetailScreen(VisualElement root)
        {
            m_Root = root;
            m_Items = root.Q<ToggleGroup>("items");
            m_Pagination = root.Q<Pagination>("pagination");
            m_DetailTitle = root.Q<Label>("detail-title");
            m_DetailDescription = root.Q<Label>("detail-description");
            m_DetailFields = root.Q("detail-fields");
            m_Confirm = root.Q<Button>("confirm");

            m_Pagination.RegisterValueChangedCallback(evt => ShowPage(evt.newValue));
            m_Items.RegisterValueChangedCallback(OnItemsChanged);
            m_Confirm.clicked += () => Debug.Log($"Gallery: ListDetailScreen confirmed item {m_Selected}");
        }

        public IReadOnlyList<ListDetailItem> items => m_Data;

        // 選んだ項目の、全件の中の index。未選択は -1
        public int selectedIndex => m_Selected;

        // 読み終えたデータを出す。selected は最初に選んでおく項目の index（-1 で未選択）。0 件なら is-empty を付ける
        public void SetItems(IReadOnlyList<ListDetailItem> data, int selected = -1)
        {
            if (selected < -1 || selected >= data.Count)
                throw new ArgumentOutOfRangeException(nameof(selected), selected, $"must be -1 or less than the item count ({data.Count})");
            m_Data = data;
            m_Selected = selected;
            m_Root.RemoveFromClassList("is-loading");
            m_Root.EnableInClassList("is-empty", data.Count == 0);
            m_Pagination.SetEnabled(true);
            // pageCount を先に、value を後に書く（Pagination の README）
            m_Pagination.pageCount = (data.Count + PageSize - 1) / PageSize;
            m_Pagination.SetValueWithoutNotify(m_Selected >= 0 ? m_Selected / PageSize : 0);
            ShowPage(m_Pagination.value);
            ShowDetail();
        }

        // 読み込み中にする。行は USS が隠し、Pagination は送れなくし、詳細は未選択にする
        public void SetLoading()
        {
            m_Root.RemoveFromClassList("is-empty");
            m_Root.AddToClassList("is-loading");
            m_Pagination.SetEnabled(false);
            m_Selected = -1;
            ShowDetail();
        }

        // 行をそのページの項目で作り直し、選んだ項目がそのページにあればその行だけを選ぶ（無ければ全部外す）
        void ShowPage(int page)
        {
            // 作り直しの途中で基底が ChangeEvent を出さないよう、外す前と足す前に、全部 false で length 64 の value にする（理由は block の README の「配線」の注記）
            var none = new ToggleButtonGroupState(0, 64);
            m_Items.SetValueWithoutNotify(none);
            m_Items.Clear();
            m_Items.SetValueWithoutNotify(none);
            var start = page * PageSize;
            var end = Math.Min(start + PageSize, m_Data.Count);
            for (var i = start; i < end; i++)
                m_Items.Add(CreateRow(m_Data[i]));

            var state = none;
            if (m_Selected >= start && m_Selected < end)
                state[m_Selected - start] = true;
            m_Items.SetValueWithoutNotify(state);
        }

        static Button CreateRow(ListDetailItem item)
        {
            var row = new Button();
            row.AddToClassList("hone-list-detail-screen__item");
            var label = new Label(item.Name);
            label.AddToClassList("hone-list-detail-screen__item-label");
            label.AddToClassList("hone-text");
            row.Add(label);
            var meta = new Label(item.Meta);
            meta.AddToClassList("hone-list-detail-screen__item-meta");
            meta.AddToClassList("hone-text");
            row.Add(meta);
            return row;
        }

        // 選んだ行があればその項目を、無ければ（選んだ行をもう一度押して外れた）未選択にする
        void OnItemsChanged(ChangeEvent<ToggleButtonGroupState> evt)
        {
            m_Selected = -1;
            var start = m_Pagination.value * PageSize;
            for (var i = 0; i < Math.Min(m_Items.childCount, evt.newValue.length); i++)
            {
                if (evt.newValue[i])
                {
                    m_Selected = start + i;
                    break;
                }
            }
            ShowDetail();
        }

        void ShowDetail()
        {
            if (m_Selected < 0)
            {
                m_DetailTitle.text = SelectPrompt;
                m_DetailDescription.text = string.Empty;
                m_DetailFields.style.display = DisplayStyle.None;
                m_Confirm.SetEnabled(false);
                return;
            }

            var item = m_Data[m_Selected];
            m_DetailTitle.text = item.Name;
            m_DetailDescription.text = item.Description;
            var values = m_DetailFields.Query<Label>(className: "hone-list-detail-screen__field-value").ToList();
            for (var i = 0; i < values.Count; i++)
                values[i].text = item.Values[i];
            m_DetailFields.style.display = DisplayStyle.Flex;
            m_Confirm.SetEnabled(true);
        }
    }
}
