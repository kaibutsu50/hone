#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hone.Sandbox.Tests
{
    // Editor で実行する前提（PanelSettings と UXML を AssetDatabase から読む）。
    // 入力は SendEvent で送る（入力経路は通らない）。(G) の測定中に Focus() しないのは、ring が border を変えて高さがずれるため。
    // text の Label の本文は rich text の <noparse> などで囲まれるので、本文との比較は label.text ではなく parsedText（タグを除いた文字列）で行う。
    // parsedText はレイアウトの後に埋まるので、Show / Advance の後は frame を進めてから読む。
    // parsedText は Advanced Text Generator（sandbox の既定）でしか実装されていない（Standard の生成器では NotImplementedException。6000.7.0b2）
    public class MessageWindowTests
    {
        const string PanelSettingsPath = "Assets/Hone/Sandbox/PanelSettings.asset";
        const string SandboxUxmlPath = "Assets/Hone/Sandbox/Sandbox.uxml";
        const int MaxWaitFrames = 60;
        const int SettleFrames = 3;

        readonly List<Object> m_Created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in m_Created)
                Object.Destroy(obj);
            m_Created.Clear();
        }

        // PanelRenderer を 1 つ作り、UI が読み込まれたら root を返す。PanelSettings は複製して、テスト間で panel の状態を共有しない
        IEnumerator CreatePanel(System.Action<VisualElement> onReady)
        {
            var panelSettings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath));
            m_Created.Add(panelSettings);
            var go = new GameObject("MessageWindowTests");
            m_Created.Add(go);
            go.SetActive(false);
            var panelRenderer = go.AddComponent<PanelRenderer>();
            panelRenderer.panelSettings = panelSettings;
            panelRenderer.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SandboxUxmlPath);
            VisualElement root = null;
            panelRenderer.RegisterUIReloadCallback((renderer, element, version) => root = element);
            go.SetActive(true);

            var frames = 0;
            while ((root == null || root.panel == null) && frames < MaxWaitFrames)
            {
                frames++;
                yield return null;
            }
            Assert.IsNotNull(root?.panel, $"panel was not ready within {MaxWaitFrames} frames");
            onReady(root);
        }

        static IEnumerator Settle()
        {
            for (var i = 0; i < SettleFrames; i++)
                yield return null;
        }

        static Label TextOf(MessageWindow window) => window.Q<Label>(className: "hone-message-window__text");

        static void Submit(MessageWindow window)
        {
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = window;
                window.SendEvent(evt);
            }
        }

        // クリックはウィンドウのどこでもよいので、子の text の Label に送り、bubble でウィンドウに届くことも確かめる
        static void Click(MessageWindow window)
        {
            var label = TextOf(window);
            using (var evt = ClickEvent.GetPooled())
            {
                evt.target = label;
                label.SendEvent(evt);
            }
        }

        // 表示済みの部分の長さ（label.text の中の、未表示の部分の始まりの位置）。全部出ているときは -1。
        // 方式（未表示の部分の前に <alpha=#00> を置く）に結び付いた読み方で、表示数が減らないことを見るためだけに使う
        static int HiddenStart(MessageWindow window) => TextOf(window).text.IndexOf("<alpha=#00>", System.StringComparison.Ordinal);

        static IEnumerator WaitRealtime(float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
                yield return null;
        }

        // (A) 2 ページで Show すると、1 ページ目の全文が出て、次のページがあるので .is-waiting が付く
        [UnityTest]
        public IEnumerator Show_TwoPages_ShowsFirstPageAndWaiting()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);

            window.Show("first", "second");
            yield return Settle();

            Assert.AreEqual(0, window.pageIndex);
            Assert.AreEqual(2, window.pageCount);
            Assert.AreEqual("first", TextOf(window).parsedText);
            Assert.IsTrue(window.ClassListContains("is-waiting"));
            Assert.IsFalse(window.ClassListContains("is-revealing"));
        }

        // (B) NavigationSubmitEvent で次のページへ進み、最後のページでは .is-waiting が付かない
        [UnityTest]
        public IEnumerator Submit_AdvancesToLastPage_NoWaiting()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);
            window.Show("first", "second");
            yield return Settle();

            Submit(window);
            yield return Settle();

            Assert.AreEqual(1, window.pageIndex);
            Assert.AreEqual("second", TextOf(window).parsedText);
            Assert.IsFalse(window.ClassListContains("is-waiting"));
            Assert.IsFalse(window.ClassListContains("is-revealing"));
        }

        // (C) 最後のページで Submit すると completed が 1 回出て、その後の Submit では出ない
        [UnityTest]
        public IEnumerator Submit_OnLastPage_FiresCompletedOnce()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            var completed = 0;
            window.completed += () => completed++;
            root.Add(window);
            window.Show("first", "second");
            yield return Settle();

            Submit(window);
            Assert.AreEqual(0, completed, "completed must not fire before the last page is advanced");
            Submit(window);
            Assert.AreEqual(1, completed);
            Submit(window);
            yield return Settle();

            Assert.AreEqual(1, completed);
            Assert.AreEqual(1, window.pageIndex, "the page stays on the last one after completed");
        }

        // (D) charactersPerSecond が 0 より大きいと .is-revealing が付き、途中の Submit で全文が出て外れる。ページは進まない
        [UnityTest]
        public IEnumerator Reveal_SubmitMidway_ShowsFullWithoutAdvancing()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow { charactersPerSecond = 10 };
            root.Add(window);
            const string page = "01234567890123456789"; // 20 文字 = 2 秒かかる

            window.Show(page, "second");
            Assert.IsTrue(window.ClassListContains("is-revealing"));
            Assert.IsFalse(window.ClassListContains("is-waiting"));
            yield return null;
            StringAssert.Contains("<alpha=#00>", TextOf(window).text, "the unrevealed part must be hidden");

            Submit(window);
            yield return Settle();

            Assert.AreEqual(0, window.pageIndex);
            Assert.IsFalse(window.ClassListContains("is-revealing"));
            Assert.IsTrue(window.ClassListContains("is-waiting"));
            Assert.AreEqual(page, TextOf(window).parsedText);
            StringAssert.DoesNotContain("<alpha", TextOf(window).text, "nothing may stay hidden once the full text is shown");

            // 次のページへ進むと、そのページの文字送りが始まる
            Submit(window);
            Assert.AreEqual(1, window.pageIndex);
            Assert.IsTrue(window.ClassListContains("is-revealing"));
            Assert.IsFalse(window.ClassListContains("is-waiting"));
        }

        // (E) 文字送りが時間で終わると .is-revealing が外れ、次のページがあれば .is-waiting が付く。速さの正確さは見ない（十分長く待つだけ）
        [UnityTest]
        public IEnumerator Reveal_FinishesByTime_RemovesRevealingAddsWaiting()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow { charactersPerSecond = 100 };
            root.Add(window);
            const string page = "0123456789"; // 10 文字 = 0.1 秒

            window.Show(page, "second");
            Assert.IsTrue(window.ClassListContains("is-revealing"));

            var deadline = Time.realtimeSinceStartup + 2f;
            while (window.ClassListContains("is-revealing") && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return Settle();

            Assert.IsFalse(window.ClassListContains("is-revealing"), "the reveal did not finish in time");
            Assert.IsTrue(window.ClassListContains("is-waiting"));
            Assert.AreEqual(0, window.pageIndex);
            Assert.AreEqual(page, TextOf(window).parsedText);
        }

        // (F) ClickEvent でも (B) と同じに進む
        [UnityTest]
        public IEnumerator Click_Advances()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);
            window.Show("first", "second");
            yield return Settle();

            Click(window);
            yield return Settle();

            Assert.AreEqual(1, window.pageIndex);
            Assert.IsFalse(window.ClassListContains("is-waiting"));
        }

        // (G) 日本語の長文で、文字送りの開始直後と全文のときで、text の Label の高さが同じ。
        // Label の幅は固定した親の中で測る（幅が内容で決まると、折り返しが起きない）
        [UnityTest]
        public IEnumerator Reveal_JapaneseLong_HeightStable()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var parent = new VisualElement();
            parent.style.width = 180;
            var window = new MessageWindow { charactersPerSecond = 10 };
            parent.Add(window);
            root.Add(parent);
            const string page = "変更内容を保存して、この画面を閉じてもよろしいですか？保存しない場合、これまでの変更内容は失われます。";

            window.Show(page);
            var label = TextOf(window);
            var frames = 0;
            while (!(label.layout.height > 0) && frames < MaxWaitFrames)
            {
                frames++;
                yield return null;
            }
            yield return Settle();
            Assert.IsTrue(window.ClassListContains("is-revealing"), "the reveal ended before the first measurement");
            var revealingHeight = label.layout.height;
            // 折り返して複数行になっていること（1 行のままだと、方式が壊れていても高さが同じになって素通りする）
            Assert.Greater(revealingHeight, label.resolvedStyle.fontSize * 2, "the text must wrap to several lines");

            Submit(window);
            yield return Settle();
            Assert.IsFalse(window.ClassListContains("is-revealing"));

            Assert.AreEqual(revealingHeight, label.layout.height);
        }

        // ページが無い状態（Show 前、空文字列と null の text）。pageCount 0、pageIndex -1 で、Advance は何も起こさない
        [Test]
        public void NoPages_EmptyText_HasNoPageAndAdvanceDoesNothing()
        {
            var window = new MessageWindow();
            var completed = 0;
            window.completed += () => completed++;

            Assert.AreEqual(-1, window.pageIndex);
            Assert.AreEqual(0, window.pageCount);
            window.Advance();
            Assert.AreEqual(0, completed);

            window.text = "page";
            Assert.AreEqual(0, window.pageIndex);
            Assert.AreEqual(1, window.pageCount);

            window.text = "";
            Assert.AreEqual(-1, window.pageIndex);
            Assert.AreEqual(0, window.pageCount);

            window.text = "page";
            window.text = null;
            Assert.AreEqual(-1, window.pageIndex);
            Assert.AreEqual(0, window.pageCount);
            window.Advance();
            Assert.AreEqual(0, completed);
            Assert.IsFalse(window.ClassListContains("is-waiting"));
            Assert.IsFalse(window.ClassListContains("is-revealing"));
        }

        // ページの文字列は rich text として解釈されない。< やタグの形も、</noparse> そのものもそのまま出る
        [UnityTest]
        public IEnumerator Show_TagLikeText_IsShownLiterally()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);
            var pages = new[] { "HP<10 <b>bold</b>", "a</noparse><b>b</b>", "a</NOPARSE><b>b</b>" };
            window.Show(pages);

            for (var i = 0; i < pages.Length; i++)
            {
                yield return Settle();
                Assert.AreEqual(pages[i], TextOf(window).parsedText, $"page {i}");
                window.Advance();
            }
        }

        // 送りの途中でも、タグの形の本文は文字のまま出る。表示済みと未表示の境目が本文の </noparse> の途中に来たときも崩れない。
        // 4 文字/秒で、1 秒後は境目が "a</n" の後、2 秒後は "a</nopars" の後になる（どちらも </noparse> の途中。frame の揺れには十分な余裕がある）
        [UnityTest]
        public IEnumerator Reveal_TagLikeText_MidReveal_IsShownLiterally()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow { charactersPerSecond = 4 };
            root.Add(window);
            const string page = "a</noparse><b>bcdefgh</b>";

            window.Show(page);
            foreach (var seconds in new[] { 1f, 1f })
            {
                yield return WaitRealtime(seconds);
                Assert.IsTrue(window.ClassListContains("is-revealing"), "the reveal ended before the measurement");
                Assert.AreEqual(page, TextOf(window).parsedText);
            }
        }

        // panel から外れたら送りを止め、そのページの全文を出した状態にする
        [UnityTest]
        public IEnumerator Detach_WhileRevealing_ShowsFullText()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow { charactersPerSecond = 10 };
            root.Add(window);
            window.Show("01234567890123456789", "second");
            Assert.IsTrue(window.ClassListContains("is-revealing"));

            window.RemoveFromHierarchy();

            Assert.IsFalse(window.ClassListContains("is-revealing"));
            Assert.IsTrue(window.ClassListContains("is-waiting"));
            Assert.AreEqual(-1, HiddenStart(window), "nothing may stay hidden after detaching");

            // 付け直しても再開しない
            root.Add(window);
            yield return Settle();
            Assert.IsFalse(window.ClassListContains("is-revealing"));
            Assert.AreEqual(0, window.pageIndex);
        }

        // completed の後に Show し直すと、もう一度 completed が出る
        [UnityTest]
        public IEnumerator Show_AfterCompleted_FiresCompletedAgain()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            var completed = 0;
            window.completed += () => completed++;
            root.Add(window);

            window.Show("first");
            Submit(window);
            Assert.AreEqual(1, completed);

            window.Show("again");
            Assert.AreEqual(0, window.pageIndex);
            Submit(window);
            Assert.AreEqual(2, completed);
        }

        // 送りの途中で Show すると、1 ページ目から出し直す。前の送りは残らない（速さを 0 にすると全文が出て、そこで止まる）
        [UnityTest]
        public IEnumerator Show_WhileRevealing_RestartsFromFirstPage()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow { charactersPerSecond = 10 };
            root.Add(window);
            window.Show("first page 0123456789", "first page 2");
            Submit(window);
            Submit(window);
            Assert.AreEqual(1, window.pageIndex);

            window.Show("next", "next 2");

            Assert.AreEqual(0, window.pageIndex);
            Assert.AreEqual(2, window.pageCount);
            Assert.IsTrue(window.ClassListContains("is-revealing"));
            window.charactersPerSecond = 0;
            yield return Settle();
            Assert.IsFalse(window.ClassListContains("is-revealing"));
            Assert.AreEqual(0, window.pageIndex);
            Assert.AreEqual("next", TextOf(window).parsedText);
        }

        // 送りの途中で速さを下げても、出ていた文字は減らない
        [UnityTest]
        public IEnumerator Reveal_SpeedDecrease_DoesNotHideShownCharacters()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow { charactersPerSecond = 40 };
            root.Add(window);
            window.Show("0123456789012345678901234567890123456789012345678901234567890123456789"); // 70 文字 = 1.75 秒

            yield return WaitRealtime(0.5f);
            Assert.IsTrue(window.ClassListContains("is-revealing"));
            var before = HiddenStart(window);
            Assert.Greater(before, 0, "some characters must be shown before the speed changes");

            window.charactersPerSecond = 1;
            yield return Settle();

            Assert.IsTrue(window.ClassListContains("is-revealing"));
            Assert.GreaterOrEqual(HiddenStart(window), before);
        }

        // 速さが NaN・無限大・極端に大きい値でも、文字送りが終わらないまま残らない
        [UnityTest]
        public IEnumerator Reveal_NonFiniteOrHugeSpeed_EndsWithFullText()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            const string page = "01234567890123456789";

            // 無限大は即時表示
            var infinite = new MessageWindow { charactersPerSecond = float.PositiveInfinity };
            root.Add(infinite);
            infinite.Show(page);
            Assert.IsFalse(infinite.ClassListContains("is-revealing"));

            // 送りの途中で NaN にすると全文が出る
            var nan = new MessageWindow { charactersPerSecond = 10 };
            root.Add(nan);
            nan.Show(page);
            nan.charactersPerSecond = float.NaN;

            // 極端に大きい値は次の tick で出きる
            var huge = new MessageWindow { charactersPerSecond = 1e30f };
            root.Add(huge);
            huge.Show(page);

            var deadline = Time.realtimeSinceStartup + 2f;
            while ((nan.ClassListContains("is-revealing") || huge.ClassListContains("is-revealing")) && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsFalse(nan.ClassListContains("is-revealing"), "NaN left the reveal running");
            Assert.IsFalse(huge.ClassListContains("is-revealing"), "a huge speed left the reveal running");
            Assert.AreEqual(-1, HiddenStart(nan));
            Assert.AreEqual(-1, HiddenStart(huge));
        }

        // panel に attach する前に Show しても、attach までの待ち時間で送りが進まない
        [UnityTest]
        public IEnumerator Show_BeforeAttach_RevealStartsOnAttach()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow { charactersPerSecond = 10 };
            window.Show("01234567890123456789"); // 20 文字 = 2 秒

            yield return WaitRealtime(2.5f);
            root.Add(window);
            yield return Settle();

            Assert.IsTrue(window.ClassListContains("is-revealing"), "the time before attaching was counted");
        }

        static Label SpeakerOf(MessageWindow window) => window.Q<Label>(className: "hone-message-window__speaker");

        // 名札が出ていること: .is-speaker-set が付き、USS で display が Flex になる。
        // C# は名札の inline の display を書かない（書くと利用者の USS で隠せなくなる）
        static void AssertSpeakerShown(MessageWindow window, string speaker, string label)
        {
            Assert.IsTrue(window.ClassListContains("is-speaker-set"), $"{label}: is-speaker-set");
            Assert.AreEqual(speaker, SpeakerOf(window).text, $"{label}: speaker text");
            Assert.AreEqual(DisplayStyle.Flex, SpeakerOf(window).resolvedStyle.display, $"{label}: display");
            Assert.AreEqual(StyleKeyword.Null, SpeakerOf(window).style.display.keyword, $"{label}: inline display");
        }

        static void AssertSpeakerHidden(MessageWindow window, string label)
        {
            Assert.IsFalse(window.ClassListContains("is-speaker-set"), $"{label}: is-speaker-set");
            Assert.AreEqual(DisplayStyle.None, SpeakerOf(window).resolvedStyle.display, $"{label}: display");
            Assert.AreEqual(StyleKeyword.Null, SpeakerOf(window).style.display.keyword, $"{label}: inline display");
        }

        // (A) Page で話者を渡すと、ページごとに名札が替わる
        [UnityTest]
        public IEnumerator Show_PagesWithSpeaker_ShowsSpeakerPerPage()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);

            window.Show(new MessageWindow.Page("first speaker", "first"), new MessageWindow.Page("second speaker", "second"));
            yield return Settle();
            AssertSpeakerShown(window, "first speaker", "page 1");
            Assert.AreEqual("first", TextOf(window).parsedText);

            window.Advance();
            yield return Settle();
            AssertSpeakerShown(window, "second speaker", "page 2");
            Assert.AreEqual("second", TextOf(window).parsedText);
        }

        // (B) 話者が null と空文字列のページ、string のページ、ページ無しでは名札が出ない。
        // 話者ありの状態から各ケースへ移って外れることを見る（前の話者が残るバグを捕まえるため）
        [UnityTest]
        public IEnumerator Show_NoSpeaker_HidesSpeaker()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);
            var withSpeaker = new MessageWindow.Page("speaker", "with speaker");

            yield return Settle();
            AssertSpeakerHidden(window, "before Show");

            window.Show(withSpeaker, new MessageWindow.Page(null, "null speaker"), new MessageWindow.Page("", "empty speaker"));
            yield return Settle();
            AssertSpeakerShown(window, "speaker", "speaker page");
            window.Advance();
            yield return Settle();
            AssertSpeakerHidden(window, "null speaker after speaker page");
            window.Advance();
            yield return Settle();
            AssertSpeakerHidden(window, "empty speaker");

            window.Show(withSpeaker);
            yield return Settle();
            window.Show("string page");
            yield return Settle();
            AssertSpeakerHidden(window, "string page after speaker page");

            window.Show(withSpeaker);
            yield return Settle();
            window.Show((string[])null);
            yield return Settle();
            AssertSpeakerHidden(window, "no pages after speaker page");
            Assert.AreEqual("", SpeakerOf(window).text, "no pages: speaker text");
        }

        // Page の text が null なら、本文は空文字列として出る（例外にならない）
        [UnityTest]
        public IEnumerator Show_PageWithNullText_ShowsEmptyText()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);

            window.Show(new MessageWindow.Page("speaker", null));
            yield return Settle();

            Assert.AreEqual("", window.text);
            AssertSpeakerShown(window, "speaker", "null text");
        }

        // (C) 文字送りの途中でも、名札は全文が出ている（名札は文字送りしない）
        [UnityTest]
        public IEnumerator Reveal_Midway_SpeakerIsShownInFull()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow { charactersPerSecond = 10 };
            root.Add(window);

            window.Show(new MessageWindow.Page("long speaker name", "01234567890123456789"));
            yield return Settle();

            Assert.IsTrue(window.ClassListContains("is-revealing"), "the reveal ended before the measurement");
            AssertSpeakerShown(window, "long speaker name", "mid reveal");
        }

        // (D) 話者がタグの形でも、名札には文字のまま出る
        [UnityTest]
        public IEnumerator Show_TagLikeSpeaker_IsShownLiterally()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);

            window.Show(new MessageWindow.Page("<b>bold</b>", "text"));
            yield return Settle();

            Assert.AreEqual("<b>bold</b>", SpeakerOf(window).parsedText);
            AssertSpeakerShown(window, "<b>bold</b>", "tag-like speaker");
        }
    }
}
#endif
