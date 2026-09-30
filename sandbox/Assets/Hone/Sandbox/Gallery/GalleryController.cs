using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Gallery
{
    public readonly struct ScriptStrings
    {
        public readonly string Script;
        public readonly string Short;
        public readonly string Long;

        public ScriptStrings(string script, string shortText, string longText)
        {
            Script = script;
            Short = shortText;
            Long = longText;
        }
    }

    // 登録されたコンポーネントの生成関数ごとに 1 列を作り、TestStrings.json の全スクリプトの文字列（短文と長文）で 1 行ずつ並べる。
    // Hone のコンポーネントは、各自が Register を呼ぶ。
    [RequireComponent(typeof(PanelRenderer))]
    public class GalleryController : MonoBehaviour
    {
        struct Entry
        {
            public string Name;
            public Func<string, VisualElement> Factory;
        }

        static readonly List<Entry> s_Entries = new List<Entry>();

        [SerializeField] TextAsset m_TestStrings;

        // 同じ name の再登録は factory を置き換える（Domain Reload 無効でも二重に並ばない）
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

        public static IReadOnlyList<ScriptStrings> ParseTestStrings(string json)
        {
            var result = new List<ScriptStrings>();
            foreach (var property in JObject.Parse(json).Properties())
            {
                var shortText = (string)property.Value["short"];
                var longText = (string)property.Value["long"];
                if (shortText == null || longText == null)
                    throw new FormatException($"TestStrings.json: '{property.Name}' needs both \"short\" and \"long\"");
                result.Add(new ScriptStrings(property.Name, shortText, longText));
            }
            return result;
        }

        // container の中身を作り直す。登録済みの全 factory を、全スクリプトの短文・長文で 1 回ずつ呼ぶ
        public static void Build(VisualElement container, IReadOnlyList<ScriptStrings> strings)
        {
            container.Clear();
            foreach (var entry in s_Entries)
            {
                var column = new VisualElement();
                column.AddToClassList("gallery-column");

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
                Debug.LogError("Gallery: ScrollView named 'gallery' is not in the UXML");
                return;
            }
            if (m_TestStrings == null)
            {
                Debug.LogError("Gallery: TestStrings is not assigned");
                return;
            }
            Build(scrollView.contentContainer, ParseTestStrings(m_TestStrings.text));
        }
    }
}
