using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Button = Hone.Button;

namespace Hone.Sandbox.Gallery
{
    // ListDetailScreen block の 1 件分のデータ。詳細の見出しは label と同じ。values は詳細の項目の行（UXML の __field）の順の値
    public readonly struct ListDetailItem
    {
        public readonly string Label;
        public readonly string Meta;
        public readonly string Description;
        public readonly IReadOnlyList<string> Values;

        public ListDetailItem(string label, string meta, string description, IReadOnlyList<string> values)
        {
            Label = label;
            Meta = meta;
            Description = description;
            Values = values;
        }
    }

    // ListDetailScreen block の配線（block の README の「配線」と同じ手順）。block は振る舞いを持たないので、Gallery はここで配線する。
    // GalleryHoneEntries が block の root の userData に入れる。テストと撮影の eval は、そこから SetItems / SetLoading を呼ぶ
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
            m_Data = data;
            m_Selected = selected >= 0 && selected < data.Count ? selected : -1;
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
            // 作り直しの途中で基底が ChangeEvent を出さないよう、外す前と足す前に、全部 false で length 64 の value にする（6000.7.0b4 で確認）。
            // 選ばれている行があるまま外すと、基底が残りの子の数の length で ChangeEvent を出すことがある（1 行目が選ばれていたときに出た。2 行目のときは出なかった）。Clear で length が 0 になり、そのまま子を足すと、足すたびに length を伸ばして ChangeEvent を出す。
            // 別のイベント（Pagination の ChangeEvent）の中で作り直すと、それらが作り直しの後に届いて選択を外してしまう
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
            var label = new Label(item.Label);
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
            for (var i = 0; i < m_Items.childCount; i++)
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
            m_DetailTitle.text = item.Label;
            m_DetailDescription.text = item.Description;
            var values = m_DetailFields.Query<Label>(className: "hone-list-detail-screen__field-value").ToList();
            for (var i = 0; i < values.Count && i < item.Values.Count; i++)
                values[i].text = item.Values[i];
            m_DetailFields.style.display = DisplayStyle.Flex;
            m_Confirm.SetEnabled(true);
        }
    }
}
