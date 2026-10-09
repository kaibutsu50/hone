using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Gallery
{
    // Hone.Sandbox.UI を参照すると、namespace Hone の中の Button は Hone.Button を指す。ここの Button は Unity 標準のもの
    using Button = UnityEngine.UIElements.Button;

    public readonly struct ScriptStrings
    {
        public readonly string Script;
        public readonly string Name;
        public readonly string Short;
        public readonly string Long;

        public ScriptStrings(string script, string name, string shortText, string longText)
        {
            Script = script;
            Name = name;
            Short = shortText;
            Long = longText;
        }
    }

    // 登録されたコンポーネントのうち、左の ListView（name `components`）で選んだ 1 つの生成関数から 1 列を作り、渡された文字列（短文と長文）で 1 行ずつ並べる。
    // Build は渡された文字列をそのまま並べる。画面では en と、dropdown で選んだ 1 スクリプトを渡す。
    // Register は sandbox 側のファイルから呼ぶ（registry/ 配下のコードには書かない。配布物が sandbox の asmdef に依存してしまう）。
    // 呼ぶ時点は [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]。
    // 左のリストは PanelRenderer の UI が読み込まれた時点の登録内容で作る（それより後の Register / Unregister はリストに反映されない）。
    // 右の列は、読み込み時と、コンポーネントまたは言語を切り替えるたびに、選んだ name の factory で作り直す。
    // 読み込み時から画面に出すには、それより前（BeforeSceneLoad）に Register する。
    [RequireComponent(typeof(PanelRenderer))]
    public class GalleryController : MonoBehaviour
    {
        struct Entry
        {
            public string Name;
            public Func<string, VisualElement> Factory;
        }

        // 常に 1 行目に出すスクリプト
        public const string BaseScript = "en";

        static readonly List<Entry> s_Entries = new List<Entry>();

        [SerializeField] TextAsset m_TestStrings;

        // dropdown の選択肢（BaseScript 以外）。index が dropdown の index と対応する
        readonly List<ScriptStrings> m_Selectable = new List<ScriptStrings>();
        DropdownField m_Language;
        ListView m_Components;
        // ListView の itemsSource と同じ内容（index が ListView の index と対応する）
        readonly List<string> m_ComponentNames = new List<string>();

        // 同じ name の再登録は factory を置き換える（Domain Reload 無効でも二重に並ばない）。
        // 異なる name の登録は残り続けるので、テストなどで登録したものは Unregister で消す。
        // factory は呼ぶたびに新しい要素を返すこと（同じインスタンスを返すと、後から追加した行に付け替わって前の行から消える）。
        public static void Register(string name, Func<string, VisualElement> factory)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name is empty", nameof(name));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            var entry = new Entry { Name = name, Factory = factory };
            var index = s_Entries.FindIndex(e => e.Name == name);
            if (index >= 0)
                s_Entries[index] = entry;
            else
                s_Entries.Add(entry);
        }

        public static void Unregister(string name)
        {
            s_Entries.RemoveAll(e => e.Name == name);
        }

        // 登録順の name の一覧。左の ListView の並びと初期選択（先頭）はこれで決まる
        public static IReadOnlyList<string> Names => s_Entries.Select(e => e.Name).ToList();

        public static IReadOnlyList<ScriptStrings> ParseTestStrings(string json)
        {
            var result = new List<ScriptStrings>();
            foreach (var property in JObject.Parse(json).Properties())
            {
                var value = property.Value as JObject;
                var name = value?["name"]?.Type == JTokenType.String ? (string)value["name"] : null;
                var shortText = value?["short"]?.Type == JTokenType.String ? (string)value["short"] : null;
                var longText = value?["long"]?.Type == JTokenType.String ? (string)value["long"] : null;
                if (name == null || shortText == null || longText == null)
                    throw new FormatException($"TestStrings.json: '{property.Name}' needs \"name\", \"short\" and \"long\" as strings");
                result.Add(new ScriptStrings(property.Name, name, shortText, longText));
            }
            return result;
        }

        // 画面に出す 2 スクリプト（BaseScript と script）を、この順で返す。
        // script が BaseScript のとき、all に script が無いとき、all に BaseScript が無いときは ArgumentException
        public static IReadOnlyList<ScriptStrings> Pick(IReadOnlyList<ScriptStrings> all, string script)
        {
            if (script == BaseScript)
                throw new ArgumentException($"'{BaseScript}' is always shown; pick another script", nameof(script));

            var baseStrings = FindScript(all, BaseScript);
            var selected = FindScript(all, script);
            if (baseStrings == null)
                throw new ArgumentException($"'{BaseScript}' is not in the test strings", nameof(all));
            if (selected == null)
                throw new ArgumentException($"'{script}' is not in the test strings", nameof(script));
            return new[] { baseStrings.Value, selected.Value };
        }

        static ScriptStrings? FindScript(IReadOnlyList<ScriptStrings> all, string script)
        {
            foreach (var s in all)
                if (s.Script == script)
                    return s;
            return null;
        }

        // container の中身を作り直す。name の factory を、渡された文字列の短文・長文で 1 回ずつ呼ぶ。name が未登録なら ArgumentException
        public static void Build(VisualElement container, string name, IReadOnlyList<ScriptStrings> strings)
        {
            var index = s_Entries.FindIndex(e => e.Name == name);
            if (index < 0)
                throw new ArgumentException($"'{name}' is not registered", nameof(name));
            var entry = s_Entries[index];

            container.Clear();

            var column = new VisualElement();
            column.AddToClassList("gallery-column");
            column.AddToClassList(ColumnClass(entry.Name));

            var heading = new Label(entry.Name);
            heading.AddToClassList("gallery-heading");
            column.Add(heading);

            foreach (var s in strings)
            {
                var row = new VisualElement();
                row.AddToClassList("gallery-row");

                var tag = new Label(s.Script);
                tag.AddToClassList("gallery-tag");
                row.Add(tag);

                row.Add(Create(entry, s.Short));
                row.Add(Create(entry, s.Long));
                column.Add(row);
            }

            container.Add(column);
        }

        public static string ColumnClass(string name) => "gallery-column--" + name.Replace('.', '-').ToLowerInvariant();

        static VisualElement Create(Entry entry, string text)
        {
            var element = entry.Factory(text);
            if (element == null)
                throw new InvalidOperationException($"Gallery: factory '{entry.Name}' returned null for \"{text}\"");
            return element;
        }

        void Awake()
        {
            Register("Label", text => new Label(text));
            Register("Button", text => new Button { text = text });
            GetComponent<PanelRenderer>().RegisterUIReloadCallback((panelRenderer, root, version) => Rebuild(root));
        }

        void Rebuild(VisualElement root)
        {
            // 前の UI を指したまま残すと、この Rebuild が途中で止まったときに SelectScript と SelectComponent が前の要素を操作してしまう。
            // null にしておけば、どちらも InvalidOperationException で eval に返る
            m_Language = null;
            m_Components = null;
            m_Selectable.Clear();
            m_ComponentNames.Clear();

            var scrollView = root.Q<ScrollView>("gallery");
            if (scrollView == null)
            {
                Debug.LogError("Gallery: ScrollView named 'gallery' was not found (the PanelRenderer's UXML is not Gallery.uxml, or it has no such element)", this);
                return;
            }
            if (m_TestStrings == null)
            {
                Debug.LogError("Gallery: TestStrings is not assigned", this);
                return;
            }
            var language = root.Q<DropdownField>("language");
            if (language == null)
            {
                Debug.LogError("Gallery: DropdownField named 'language' was not found (the PanelRenderer's UXML is not Gallery.uxml, or it has no such element)", this);
                return;
            }
            var components = root.Q<ListView>("components");
            if (components == null)
            {
                Debug.LogError("Gallery: ListView named 'components' was not found (the PanelRenderer's UXML is not Gallery.uxml, or it has no such element)", this);
                return;
            }
            var names = Names.ToList();
            if (names.Count == 0)
            {
                Debug.LogError("Gallery: no component is registered", this);
                return;
            }

            var all = ParseTestStrings(m_TestStrings.text);
            var selectable = all.Where(s => s.Script != BaseScript).ToList();
            // 例外を出しうる Pick と初回の Build を、dropdown と ListView を配線する前に済ませる。
            // 失敗したときに、配線済みで右が空の半端な状態を残さない（m_Language と m_Components が null のままなので、SelectScript と SelectComponent が例外を返す）
            Build(scrollView.contentContainer, names[0], Pick(all, selectable[0].Script));

            // 選んでいるコンポーネントを、今の言語で作り直す。コンポーネントと言語のどちらを切り替えても、もう一方は今のまま
            void BuildSelected()
            {
                var index = components.selectedIndex;
                if (index < 0)
                    return;
                Build(scrollView.contentContainer, names[index], Pick(all, selectable[language.index].Script));
            }

            language.choices = selectable.Select(s => s.Name).ToList();
            language.SetValueWithoutNotify(language.choices[0]);
            language.RegisterValueChangedCallback(_ => BuildSelected());
            m_Selectable.AddRange(selectable);
            m_Language = language;

            // itemsSource は makeItem と bindItem の後に入れる（先に入れると既定の項目で一度作ってから作り直す）
            components.makeItem = () =>
            {
                var item = new Label();
                item.AddToClassList("gallery-components__item");
                return item;
            };
            components.bindItem = (element, index) => ((Label)element).text = names[index];
            components.itemsSource = names;
            components.selectionType = SelectionType.Single;
            components.SetSelectionWithoutNotify(new[] { 0 });
            components.selectedIndicesChanged += _ => BuildSelected();
            m_ComponentNames.AddRange(names);
            m_Components = components;
        }

        // dropdown で script を選んだのと同じ経路（ChangeEvent）で切り替える。eval から言語を切り替えて撮るための入口。
        // すでに選ばれている script を渡すと、ChangeEvent が出ないので何も起きない
        public void SelectScript(string script)
        {
            if (m_Language == null || m_Language.panel == null)
                throw new InvalidOperationException("Gallery: the UI is not loaded yet");

            var index = m_Selectable.FindIndex(s => s.Script == script);
            if (index < 0)
                throw new ArgumentException($"'{script}' is not selectable", nameof(script));
            m_Language.index = index;
        }

        // ListView で name を選んだのと同じ経路で切り替える。eval からコンポーネントを切り替えて撮るための入口。
        // すでに選ばれている name を渡すと、選択が変わらないので何も起きない
        public void SelectComponent(string name)
        {
            if (m_Components == null || m_Components.panel == null)
                throw new InvalidOperationException("Gallery: the UI is not loaded yet");

            var index = m_ComponentNames.IndexOf(name);
            if (index < 0)
                throw new ArgumentException($"'{name}' is not in the component list", nameof(name));
            m_Components.selectedIndex = index;
        }
    }
}
