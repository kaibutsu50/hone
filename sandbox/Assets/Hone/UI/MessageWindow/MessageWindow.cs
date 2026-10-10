using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine.UIElements;

namespace Hone
{
    // 複数ページの文章を 1 ページずつ出し、Submit（NavigationSubmitEvent）かクリック（ClickEvent）で次へ送る。
    // C# では Hone.Core（FocusScope、BackStack）を使わない。見た目は Core.uss の .hone-focusable（ring）と .hone-text（本文フォント）に依存する。
    // 文字送りは text の Label を rich text で組み替えて行う: 表示済みの部分はそのまま、未表示の部分を <alpha=#00> で透明にする。
    // 未表示の文字も描かれる（透明なだけ）ので、行の折り返しと高さは送りの途中でも全文のときと同じになる。
    // 演出（速さ、▼ の点滅、送りの音）は持たない。.is-revealing と .is-waiting の状態クラスを付け外しするだけで、速さは charactersPerSecond で受け取る。
    //
    // 話者の名札は、ページごとに Page で渡す。名札は文字送りせず、ページの開始時に全文を出す。表示の切り替えは .is-speaker-set と USS に任せる（C# から display を書かない）。
    //
    // 構造: MessageWindow（.hone-message-window）> 名札の Label（.hone-message-window__speaker）+ text の Label（.hone-message-window__text）+ ▼ の Label（.hone-message-window__indicator）。
    // slot は持たず、UXML の子要素は想定しない（contentContainer は自分自身なので、入れると ▼ の後ろに並ぶ）。
    [UxmlElement]
    public partial class MessageWindow : VisualElement
    {
        // 1 ページ分。値は渡したまま持つ。Show は、speaker が null か空文字列のページを話者なし（名札は出ない）、text が null のページを空文字列として扱う
        public readonly struct Page
        {
            public readonly string speaker;
            public readonly string text;

            public Page(string speaker, string text)
            {
                this.speaker = speaker;
                this.text = text;
            }
        }

        const long TickIntervalMs = 16;

        // 文字送りの速さ（1 秒あたりに出す文字数）。ページの開始時に 0 以下（NaN、無限大を含む）なら即時表示。
        // 文字送りの途中で変えると、その時点から新しい速さで進む（出ている文字は減らない）。0 以下にすると全文が出る。
        // UXML の text より前に適用されるよう、text より先に宣言している（UXML の属性はクラスでの宣言順に適用される。6000.7.0b2 で確認）
        [UxmlAttribute]
        public float charactersPerSecond { get; set; }

        // 今のページの文字列（ページが無いときは空文字列）。set は Show(value) と同じで、空文字列と null はページ無し。
        // 1 ページだけを出す UXML での確認用
        [UxmlAttribute]
        public string text
        {
            get => m_PageIndex >= 0 ? m_Pages[m_PageIndex].text : "";
            set
            {
                if (string.IsNullOrEmpty(value))
                    Show((string[])null);
                else
                    Show(value);
            }
        }

        // 今のページ。ページが無いときは -1
        public int pageIndex => m_PageIndex;

        public int pageCount => m_Pages.Length;

        // 最後のページで、全文が出た後に Advance() されたとき 1 回。次の Show まで再び出ない
        public event Action completed;

        readonly Label m_Speaker;
        readonly Label m_Text;

        Page[] m_Pages = Array.Empty<Page>();
        int m_PageIndex = -1;
        // 今のページの text element の先頭の UTF-16 位置。数が text element の数
        int[] m_ElementStarts = Array.Empty<int>();
        bool m_Revealing;
        bool m_Completed;
        // 文字送りの進み（出した text element の数。小数を含む）。速さを変えても減らないよう、tick ごとに足し上げる
        double m_Progress;
        int m_Visible;
        IVisualElementScheduledItem m_Ticker;

        public MessageWindow()
        {
            AddToClassList("hone-message-window");
            AddToClassList("hone-focusable");
            focusable = true;
            tabIndex = 0;

            // 話者は rich text として解釈しない（<b> などのタグの形も文字のまま出す）
            m_Speaker = new Label { enableRichText = false };
            m_Speaker.AddToClassList("hone-message-window__speaker");
            m_Speaker.AddToClassList("hone-text");
            Add(m_Speaker);

            m_Text = new Label();
            m_Text.AddToClassList("hone-message-window__text");
            m_Text.AddToClassList("hone-text");
            Add(m_Text);

            var indicator = new Label("▼");
            indicator.AddToClassList("hone-message-window__indicator");
            indicator.AddToClassList("hone-text");
            Add(indicator);

            RegisterCallback<NavigationSubmitEvent>(OnSubmit);
            RegisterCallback<ClickEvent>(OnClick);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        // 話者なしのページとして Show(params Page[]) と同じに扱う。null のページは空文字列として扱う
        public void Show(params string[] pages)
        {
            if (pages == null)
            {
                Show((Page[])null);
                return;
            }
            var converted = new Page[pages.Length];
            for (var i = 0; i < pages.Length; i++)
                converted[i] = new Page(null, pages[i]);
            Show(converted);
        }

        // 1 ページ目から出し直す。送りの途中でも最初からやり直す。null と空の配列はページ無し。text が null のページは空文字列として扱う。
        // null は型を付けて渡す（Show(null) と引数なしの Show() は、2 つの Show のどちらか決まらずコンパイルエラー）
        public void Show(params Page[] pages)
        {
            StopReveal();
            m_Completed = false;
            if (pages == null || pages.Length == 0)
            {
                m_Pages = Array.Empty<Page>();
                m_PageIndex = -1;
                m_ElementStarts = Array.Empty<int>();
                m_Speaker.text = "";
                m_Text.text = "";
                UpdateStateClasses();
                return;
            }
            m_Pages = new Page[pages.Length];
            for (var i = 0; i < pages.Length; i++)
                m_Pages[i] = new Page(pages[i].speaker, pages[i].text ?? "");
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

        // 文字送りをする速さか。NaN は比較が false になるので即時表示に入る。無限大は「すぐ全部」の意味として即時表示に寄せる
        bool RevealsGradually => charactersPerSecond > 0 && !float.IsPositiveInfinity(charactersPerSecond);

        void StartPage(int index)
        {
            StopReveal();
            m_PageIndex = index;
            var page = m_Pages[index].text;
            m_Speaker.text = m_Pages[index].speaker ?? "";
            m_ElementStarts = StringInfo.ParseCombiningCharacters(page);
            if (RevealsGradually && m_ElementStarts.Length > 0)
            {
                m_Revealing = true;
                m_Progress = 0;
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
            if (!RevealsGradually)
            {
                FinishReveal();
                return;
            }
            m_Progress += state.deltaTime * (double)charactersPerSecond / 1000.0;
            // int に直す前に範囲を確かめる（大きすぎる値を int にすると壊れる）
            if (!(m_Progress < m_ElementStarts.Length))
            {
                FinishReveal();
                return;
            }
            var visible = (int)Math.Floor(m_Progress);
            if (visible != m_Visible)
                Render(visible);
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
            m_Visible = visibleElements;
            var page = m_Pages[m_PageIndex].text;
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
            EnableInClassList("is-speaker-set", m_PageIndex >= 0 && !string.IsNullOrEmpty(m_Pages[m_PageIndex].speaker));
            EnableInClassList("is-revealing", m_Revealing);
            EnableInClassList("is-waiting", !m_Revealing && m_PageIndex >= 0 && m_PageIndex < m_Pages.Length - 1);
        }

        // Submit とクリックは、この部品が 1 歩進める入力として消費し、祖先には届けない
        void OnSubmit(NavigationSubmitEvent evt)
        {
            Advance();
            evt.StopPropagation();
        }

        void OnClick(ClickEvent evt)
        {
            Advance();
            evt.StopPropagation();
        }

        // panel から外れたら送りを止め、そのページの全文を出した状態にする（付け直しても再開しない）
        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            if (m_Revealing)
                FinishReveal();
        }
    }
}
