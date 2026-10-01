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

    // 登録されたコンポーネントの生成関数ごとに 1 列を作り、渡された文字列（短文と長文）で 1 行ずつ並べる。
    // Build は渡された文字列をそのまま並べる。画面では en と、dropdown で選んだ 1 スクリプトを渡す。
    // Register は sandbox 側のファイルから呼ぶ（registry/ 配下のコードには書かない。配布物が sandbox の asmdef に依存してしまう）。
    // 呼ぶ時点は [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]。
    // 列を作るのは PanelRenderer の UI が読み込まれた時点の 1 回だけで、それより後の Register は画面に出ない。
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

        // 画面に出す 2 スクリプト（BaseScript と script）を、この順で返す
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

        // container の中身を作り直す。登録済みの全 factory を、渡された文字列の短文・長文で 1 回ずつ呼ぶ
        public static void Build(VisualElement container, IReadOnlyList<ScriptStrings> strings)
        {
            container.Clear();
            foreach (var entry in s_Entries)
            {
                var column = new VisualElement();
                column.AddToClassList("gallery-column");
                // 列ごとに幅を変えるためのクラス。name の "." を "-" にして小文字にする（例: Hone.Dialog → gallery-column--hone-dialog）
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

            var all = ParseTestStrings(m_TestStrings.text);
            m_Selectable.Clear();
            foreach (var s in all)
                if (s.Script != BaseScript)
                    m_Selectable.Add(s);

            language.choices = m_Selectable.Select(s => s.Name).ToList();
            language.SetValueWithoutNotify(language.choices[0]);
            language.RegisterValueChangedCallback(_ =>
                Build(scrollView.contentContainer, Pick(all, m_Selectable[language.index].Script)));
            m_Language = language;

            Build(scrollView.contentContainer, Pick(all, m_Selectable[0].Script));
        }

        // dropdown で script を選んだのと同じ経路（ChangeEvent）で切り替える。eval から言語を切り替えて撮るための入口
        public void SelectScript(string script)
        {
            if (m_Language == null)
                throw new InvalidOperationException("Gallery: the UI is not loaded yet");

            var index = m_Selectable.FindIndex(s => s.Script == script);
            if (index < 0)
                throw new ArgumentException($"'{script}' is not selectable", nameof(script));
            m_Language.index = index;
        }
    }
}
