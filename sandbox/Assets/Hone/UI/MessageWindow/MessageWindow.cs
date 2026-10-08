using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine.UIElements;

namespace Hone
{
    // 複数ページの文章を 1 ページずつ出し、Submit（NavigationSubmitEvent）かクリック（ClickEvent）で次へ送る。単独で完結し、Core には依存しない。
    // 文字送りは text の Label を rich text で組み替えて行う: 表示済みの部分はそのまま、未表示の部分を <alpha=#00> で透明にする。
    // 未表示の文字も描かれる（透明なだけ）ので、行の折り返しと高さは送りの途中でも全文のときと同じになる。
    // 演出（速さ、▼ の点滅、送りの音）は持たない。.is-revealing と .is-waiting の状態クラスを付け外しするだけで、速さは charactersPerSecond で受け取る。
    //
    // 構造: MessageWindow（.hone-message-window）> text の Label（.hone-message-window__text）+ ▼ の Label（.hone-message-window__indicator）。
    // slot（contentContainer）は持たない。UXML の子要素は受け付けない。
    [UxmlElement]
    public partial class MessageWindow : VisualElement
    {
        const long TickIntervalMs = 16;

        // 文字送りの速さ（1 秒あたりに出す文字数）。0 以下は即時表示。Show と、次のページへ進む時点の値が使われる。
        // 文字送りの途中で変えても反映される（0 以下にすると全文が出る）。
        // UXML の text より前に適用されるよう、text より先に宣言している
        [UxmlAttribute]
        public float charactersPerSecond { get; set; }

        // 今のページの文字列（ページが無いときは空文字列）。set は Show(value) と同じで、空文字列と null はページ無し。
        // 1 ページだけを出す UXML での確認用
        [UxmlAttribute]
        public string text
        {
            get => m_PageIndex >= 0 ? m_Pages[m_PageIndex] : "";
            set
            {
                if (string.IsNullOrEmpty(value))
                    Show(null);
                else
                    Show(value);
            }
        }

        // 今のページ。ページが無いときは -1
        public int pageIndex => m_PageIndex;

        public int pageCount => m_Pages.Length;

        // 最後のページで、全文が出た後に Advance() されたとき 1 回。次の Show まで再び出ない
        public event Action completed;

        readonly Label m_Text;

        string[] m_Pages = Array.Empty<string>();
        int m_PageIndex = -1;
        // 今のページの text element の先頭の UTF-16 位置と、その数
        int[] m_ElementStarts = Array.Empty<int>();
        bool m_Revealing;
        bool m_Completed;
        long m_ElapsedMs;
        IVisualElementScheduledItem m_Ticker;

        public MessageWindow()
        {
            AddToClassList("hone-message-window");
            AddToClassList("hone-focusable");
            focusable = true;
            tabIndex = 0;

            m_Text = new Label();
            m_Text.AddToClassList("hone-message-window__text");
            m_Text.AddToClassList("hone-text");
            // contentContainer は差し替えていないので、Add でそのまま入る
            Add(m_Text);

            var indicator = new Label("▼");
            indicator.AddToClassList("hone-message-window__indicator");
            indicator.AddToClassList("hone-text");
            Add(indicator);

            RegisterCallback<NavigationSubmitEvent>(OnSubmit);
            RegisterCallback<ClickEvent>(OnClick);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        // 1 ページ目から出し直す。送りの途中でも最初からやり直す。null と空の配列はページ無し。null のページは空文字列として扱う
        public void Show(params string[] pages)
        {
            StopReveal();
            m_Completed = false;
            if (pages == null || pages.Length == 0)
            {
                m_Pages = Array.Empty<string>();
                m_PageIndex = -1;
                m_ElementStarts = Array.Empty<int>();
                m_Text.text = "";
                UpdateStateClasses();
                return;
            }
            m_Pages = new string[pages.Length];
            for (var i = 0; i < pages.Length; i++)
                m_Pages[i] = pages[i] ?? "";
            StartPage(0);
        }

        // Submit とクリックと同じ処理。文字送りの途中なら全文を出し、全文が出ていれば次のページへ進む。
        // 最後のページで全文が出ていれば completed を 1 回出す。ページが無いときと completed の後は何もしない
        public void Advance()
        {
            if (m_PageIndex < 0)
                return;
            if (m_Revealing)
            {
                FinishReveal();
                return;
            }
            if (m_PageIndex < m_Pages.Length - 1)
            {
                StartPage(m_PageIndex + 1);
                return;
            }
            if (m_Completed)
                return;
            m_Completed = true;
            completed?.Invoke();
        }

        void StartPage(int index)
        {
            StopReveal();
            m_PageIndex = index;
            var page = m_Pages[index];
            m_ElementStarts = StringInfo.ParseCombiningCharacters(page);
            if (charactersPerSecond > 0 && m_ElementStarts.Length > 0)
            {
                m_Revealing = true;
                m_ElapsedMs = 0;
                Render(0);
                m_Ticker = schedule.Execute(OnTick).Every(TickIntervalMs);
            }
            else
            {
                Render(m_ElementStarts.Length);
            }
            UpdateStateClasses();
        }

        void OnTick(TimerState state)
        {
            if (!m_Revealing)
                return;
            if (charactersPerSecond <= 0)
            {
                FinishReveal();
                return;
            }
            m_ElapsedMs += state.deltaTime;
            var visible = (long)Math.Floor(m_ElapsedMs * (double)charactersPerSecond / 1000.0);
            if (visible >= m_ElementStarts.Length)
            {
                FinishReveal();
                return;
            }
            Render((int)visible);
        }

        // 送りを止めて、今のページの全文を出した状態にする
        void FinishReveal()
        {
            StopReveal();
            Render(m_ElementStarts.Length);
            UpdateStateClasses();
        }

        void StopReveal()
        {
            m_Revealing = false;
            m_Ticker?.Pause();
            m_Ticker = null;
        }

        // 先頭から visibleElements 個の text element を出す。全部出すときは未表示の部分が無いので、alpha のタグを付けない
        void Render(int visibleElements)
        {
            var page = m_Pages[m_PageIndex];
            if (visibleElements >= m_ElementStarts.Length)
            {
                m_Text.text = NoParse(page);
                return;
            }
            var head = visibleElements == 0 ? 0 : m_ElementStarts[visibleElements];
            m_Text.text = NoParse(page.Substring(0, head)) + "<alpha=#00>" + NoParse(page.Substring(head));
        }

        // 本文の < をタグとして解釈させないよう <noparse> で囲む。
        // 本文の </noparse> は noparse の中でも閉じタグになる（大文字小文字を問わない）ので、"<" だけ noparse の中に残し、"/noparse>" を外に出して文字のまま出す
        static string NoParse(string text) =>
            text.Length == 0
                ? ""
                : "<noparse>" + Regex.Replace(text, "</noparse>", m => "<</noparse>" + m.Value.Substring(1) + "<noparse>", RegexOptions.IgnoreCase) + "</noparse>";

        void UpdateStateClasses()
        {
            EnableInClassList("is-revealing", m_Revealing);
            EnableInClassList("is-waiting", !m_Revealing && m_PageIndex >= 0 && m_PageIndex < m_Pages.Length - 1);
        }

        void OnSubmit(NavigationSubmitEvent evt)
        {
            Advance();
            evt.StopPropagation();
        }

        void OnClick(ClickEvent evt)
        {
            Advance();
        }

        // panel から外れたら送りを止め、そのページの全文を出した状態にする（付け直しても再開しない）
        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            if (m_Revealing)
                FinishReveal();
        }
    }
}
