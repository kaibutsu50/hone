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

        // (A) className 付きのページを出している間だけ、窓にそのクラスが付く。className が null のページへ進むと外れる
        [UnityTest]
        public IEnumerator Show_PageWithClassName_AddsClassOnlyWhileShown()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);

            window.Show(new MessageWindow.Page(null, "a", "page-a"), new MessageWindow.Page(null, "b"));
            yield return Settle();
            Assert.IsTrue(window.ClassListContains("page-a"), "page 1");

            window.Advance();
            yield return Settle();
            Assert.IsFalse(window.ClassListContains("page-a"), "page 2 (no className)");
        }

        // (B) className の違うページへ進むと、前のクラスが外れて新しいクラスが付く
        [UnityTest]
        public IEnumerator Show_ClassNameChanges_SwapsClass()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);

            window.Show(new MessageWindow.Page(null, "a", "page-a"), new MessageWindow.Page(null, "b", "page-b"));
            yield return Settle();
            Assert.IsTrue(window.ClassListContains("page-a"), "page 1");

            window.Advance();
            yield return Settle();
            Assert.IsTrue(window.ClassListContains("page-b"), "page 2: new class");
            Assert.IsFalse(window.ClassListContains("page-a"), "page 2: previous class");
        }

        // 同じ className のページへ進んでも、クラスは付いたまま
        [UnityTest]
        public IEnumerator Show_SameClassName_KeepsClass()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);

            window.Show(new MessageWindow.Page(null, "a", "page-a"), new MessageWindow.Page(null, "b", "page-a"));
            yield return Settle();
            window.Advance();
            yield return Settle();

            Assert.AreEqual(1, window.pageIndex);
            Assert.IsTrue(window.ClassListContains("page-a"), "page 2 (same className)");
        }

        // className が空文字列のページでは、クラスが増えない
        [UnityTest]
        public IEnumerator Show_EmptyClassName_AddsNoClass()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);
            var before = new List<string>(window.GetClasses()).Count;

            window.Show(new MessageWindow.Page(null, "a", ""));
            yield return Settle();

            Assert.AreEqual(before, new List<string>(window.GetClasses()).Count);
        }

        // completed の後に別のページで Show し直すと、前のクラスが外れる
        [UnityTest]
        public IEnumerator Show_AfterCompleted_RemovesPreviousClass()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);
            var completed = 0;
            window.completed += () => completed++;

            window.Show(new MessageWindow.Page(null, "a", "page-a"));
            yield return Settle();
            window.Advance();
            Assert.AreEqual(1, completed);

            window.Show(new MessageWindow.Page(null, "c", "page-c"));
            yield return Settle();
            Assert.IsTrue(window.ClassListContains("page-c"), "new class");
            Assert.IsFalse(window.ClassListContains("page-a"), "previous class");
        }

        // (C) ページ無しにしたとき、と、別のページで Show し直したときに、前のクラスが外れる
        [UnityTest]
        public IEnumerator Show_NoPagesOrOtherPages_RemovesPreviousClass()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);

            window.Show(new MessageWindow.Page(null, "a", "page-a"));
            yield return Settle();
            Assert.IsTrue(window.ClassListContains("page-a"), "before");

            window.Show((MessageWindow.Page[])null);
            yield return Settle();
            Assert.IsFalse(window.ClassListContains("page-a"), "no pages");

            window.Show(new MessageWindow.Page(null, "a", "page-a"));
            yield return Settle();
            window.Show(new MessageWindow.Page(null, "c", "page-c"));
            yield return Settle();
            Assert.IsTrue(window.ClassListContains("page-c"), "Show again: new class");
            Assert.IsFalse(window.ClassListContains("page-a"), "Show again: previous class");
        }

        // (D) 文字送りの途中（.is-revealing が付いている間）から、ページのクラスが付いている
        [UnityTest]
        public IEnumerator Show_PageClassWhileRevealing_AppliesFromStart()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow { charactersPerSecond = 1 };
            root.Add(window);

            window.Show(new MessageWindow.Page(null, "0123456789", "page-a"));

            Assert.IsTrue(window.ClassListContains("is-revealing"), "is-revealing");
            Assert.IsTrue(window.ClassListContains("page-a"), "page-a while revealing");
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

        // 選択肢。コメントの (A)〜(F) は Issue #128 の受け入れ条件の番号。選択肢の Button は .hone-message-window__choices の子
        static List<Button> ChoicesOf(MessageWindow window) =>
            window.Q(className: "hone-message-window__choices").Query<Button>().ToList();

        static void SubmitTo(VisualElement target)
        {
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        static void ClickTo(VisualElement target)
        {
            using (var evt = ClickEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        // ポインタで押して離す（Button の clicked は ClickEvent ではなく、この 2 つで出る）。座標は DialogTests の Click と同じく target の左上の内側
        static void Press(VisualElement target)
        {
            var position = target.worldBound.min + new Vector2(2, 2);
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = position, button = 0, clickCount = 1 }))
            {
                down.target = target;
                target.SendEvent(down);
            }
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = position, button = 0, clickCount = 1 }))
            {
                up.target = target;
                target.SendEvent(up);
            }
        }

        static MessageWindow.Page ChoicePage(string text, params string[] choices) => new MessageWindow.Page(null, text, choices: choices);

        // (A) choices が空の配列か 3 個以上なら ArgumentException。null と 1 個と 2 個は作れる
        [Test]
        public void Page_EmptyOrThreeChoices_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => new MessageWindow.Page(null, "text", choices: new string[0]), "empty");
            Assert.Throws<System.ArgumentException>(() => new MessageWindow.Page(null, "text", choices: new[] { "a", "b", "c" }), "three");
            Assert.DoesNotThrow(() => new MessageWindow.Page(null, "text", choices: null), "null");
            Assert.DoesNotThrow(() => new MessageWindow.Page(null, "text", choices: new[] { "a" }), "one");
            Assert.DoesNotThrow(() => new MessageWindow.Page(null, "text", choices: new[] { "a", "b" }), "two");
        }

        // (B) 選択肢 1 個と 2 個のページで、全文が出ると選択肢の Button（outline）が出て、is-choosing が付き、is-waiting は付かない（次のページがあっても）
        [UnityTest]
        public IEnumerator Show_PageWithChoices_ShowsButtonsAndChoosing([Values(1, 2)] int count)
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);
            var choices = count == 1 ? new[] { "yes" } : new[] { "yes", "no" };

            window.Show(ChoicePage("question", choices), new MessageWindow.Page(null, "next"));
            yield return Settle();

            var buttons = ChoicesOf(window);
            Assert.AreEqual(count, buttons.Count);
            for (var i = 0; i < count; i++)
            {
                Assert.AreEqual(choices[i], buttons[i].text);
                Assert.AreEqual(Button.Variant.Outline, buttons[i].variant);
                Assert.IsTrue(buttons[i].ClassListContains("hone-message-window__choice"));
            }
            Assert.IsTrue(window.ClassListContains("is-choosing"));
            Assert.IsFalse(window.ClassListContains("is-waiting"));
        }

        // (B) 文字送りの途中は選択肢を出さず、全文が出たところで出す
        [UnityTest]
        public IEnumerator Reveal_PageWithChoices_ShowsButtonsAfterFullText()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow { charactersPerSecond = 10 };
            root.Add(window);

            window.Show(ChoicePage("01234567890123456789", "yes", "no"));
            yield return Settle();
            Assert.IsTrue(window.ClassListContains("is-revealing"), "the reveal ended before the first measurement");
            Assert.AreEqual(0, ChoicesOf(window).Count);
            Assert.IsFalse(window.ClassListContains("is-choosing"));

            Submit(window);
            yield return Settle();

            Assert.AreEqual(2, ChoicesOf(window).Count);
            Assert.IsTrue(window.ClassListContains("is-choosing"));
            Assert.IsFalse(window.ClassListContains("is-revealing"));
        }

        // (C) is-choosing の間、窓への Submit と、選択肢の外（本文）のクリックと、Advance() では進まない
        [UnityTest]
        public IEnumerator Choosing_SubmitOrClickOnWindow_DoesNotAdvance()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            var selected = 0;
            var completed = 0;
            window.choiceSelected += index => selected++;
            window.completed += () => completed++;
            root.Add(window);
            window.Show(ChoicePage("question", "yes", "no"), new MessageWindow.Page(null, "next"));
            yield return Settle();

            Submit(window);
            Click(window);
            window.Advance();
            yield return Settle();

            Assert.AreEqual(0, window.pageIndex);
            Assert.IsTrue(window.ClassListContains("is-choosing"));
            Assert.AreEqual(2, ChoicesOf(window).Count);
            Assert.AreEqual(0, selected);
            Assert.AreEqual(0, completed);
        }

        // (C) 選択肢の Button への ClickEvent だけでは選ばれず（Button の clicked は出ない）、祖先へも伝わらない
        [UnityTest]
        public IEnumerator Choosing_ClickEventOnChoice_DoesNotSelectOrReachAncestors()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            var bubbled = 0;
            root.RegisterCallback<ClickEvent>(evt => bubbled++);
            root.Add(window);
            window.Show(ChoicePage("question", "yes", "no"), new MessageWindow.Page(null, "next"));
            yield return Settle();

            ClickTo(ChoicesOf(window)[0]);
            yield return Settle();

            Assert.AreEqual(0, window.pageIndex);
            Assert.IsTrue(window.ClassListContains("is-choosing"));
            Assert.AreEqual(0, bubbled, "the click on a choice must not reach the ancestors");
        }

        // 選択肢の Button の子孫が target の ClickEvent では、窓は進まず、祖先へも伝わらない（合成した ClickEvent では Button の clicked は出ない）。
        // 押した処理で Button が外れた後に届く ClickEvent は窓まで届かないので、祖先を辿る判定そのものは、このテストでも Submit 側のテストでも区別できない（古い判定の実装に戻しても通ることを確かめた）
        [UnityTest]
        public IEnumerator Choosing_ClickEventOnChoiceChild_DoesNotAdvanceOrReachAncestors()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            var bubbled = 0;
            var selected = 0;
            root.RegisterCallback<ClickEvent>(evt => bubbled++);
            window.choiceSelected += index => selected++;
            root.Add(window);
            window.Show(ChoicePage("question", "yes", "no"), new MessageWindow.Page(null, "next"));
            yield return Settle();
            var child = new VisualElement();
            var grandchild = new VisualElement();
            child.Add(grandchild);
            ChoicesOf(window)[0].Add(child);

            ClickTo(grandchild);
            yield return Settle();

            Assert.AreEqual(0, window.pageIndex);
            Assert.IsTrue(window.isChoosing);
            Assert.AreEqual(0, selected);
            Assert.AreEqual(0, bubbled, "the click on a child of a choice must not reach the ancestors");
        }

        // 選択肢の Button の子孫が target の NavigationSubmitEvent では、Button が 1 回選び、窓の側の処理は重ねて進めない（次のページで止まる）
        [UnityTest]
        public IEnumerator Choosing_SubmitOnChoiceChild_SelectsOnceWithoutExtraAdvance()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            var selected = new List<int>();
            var choosingInHandler = true;
            window.choiceSelected += index => { selected.Add(index); choosingInHandler = window.isChoosing; };
            root.Add(window);
            window.Show(ChoicePage("question", "yes", "no"), new MessageWindow.Page(null, "second"), new MessageWindow.Page(null, "third"));
            yield return Settle();
            var child = new VisualElement();
            var grandchild = new VisualElement();
            child.Add(grandchild);
            ChoicesOf(window)[1].Add(child);

            SubmitTo(grandchild);
            yield return Settle();

            CollectionAssert.AreEqual(new[] { 1 }, selected);
            Assert.AreEqual(1, window.pageIndex);
            Assert.IsFalse(window.isChoosing);
            Assert.IsFalse(choosingInHandler, "isChoosing inside choiceSelected");
        }

        // isChoosing は選択肢を出している間だけ true
        [UnityTest]
        public IEnumerator IsChoosing_TrueOnlyWhileChoicesShown()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);
            Assert.IsFalse(window.isChoosing, "no page");

            window.Show(new MessageWindow.Page(null, "plain"), ChoicePage("question", "yes", "no"), new MessageWindow.Page(null, "last"));
            yield return Settle();
            Assert.IsFalse(window.isChoosing, "page without choices");

            window.Advance();
            yield return Settle();
            Assert.IsTrue(window.isChoosing, "choices shown");
            Assert.AreEqual(window.ClassListContains("is-choosing"), window.isChoosing);

            SubmitTo(ChoicesOf(window)[0]);
            yield return Settle();
            Assert.IsFalse(window.isChoosing, "after choosing");
        }

        // Show の後に元の choices 配列を書き換えても、これから出る選択肢の文字列は変わらない
        [UnityTest]
        public IEnumerator Show_MutatingChoicesArrayAfterShow_DoesNotChangeChoices()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);
            var choices = new[] { "yes", "no" };
            window.Show(new MessageWindow.Page(null, "first"), ChoicePage("question", choices));
            choices[0] = "changed";
            choices[1] = "changed";
            yield return Settle();

            window.Advance();
            yield return Settle();

            var buttons = ChoicesOf(window);
            Assert.AreEqual(2, buttons.Count);
            Assert.AreEqual("yes", buttons[0].text);
            Assert.AreEqual("no", buttons[1].text);
        }

        // (D) 選択肢への Submit で、choiceSelected がその index で 1 回出て、次のページへ 1 つだけ進む（窓への泡立ちでもう 1 つ進まない）
        [UnityTest]
        public IEnumerator Choice_Submit_FiresChoiceSelectedAndAdvancesOnce()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            var selected = new List<int>();
            window.choiceSelected += selected.Add;
            root.Add(window);
            window.Show(ChoicePage("question", "yes", "no"), new MessageWindow.Page(null, "second"), new MessageWindow.Page(null, "third"));
            yield return Settle();

            SubmitTo(ChoicesOf(window)[1]);
            yield return Settle();

            CollectionAssert.AreEqual(new[] { 1 }, selected);
            Assert.AreEqual(1, window.pageIndex);
            Assert.AreEqual("second", TextOf(window).parsedText);
            Assert.IsFalse(window.ClassListContains("is-choosing"));
            Assert.AreEqual(0, ChoicesOf(window).Count);
            Assert.IsTrue(window.ClassListContains("is-waiting"));
        }

        // (D) 選択肢をポインタで押しても同じ（clicked で 1 回選び、次のページへ 1 つだけ進む）
        [UnityTest]
        public IEnumerator Choice_Press_FiresChoiceSelectedAndAdvancesOnce()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            var selected = new List<int>();
            window.choiceSelected += selected.Add;
            root.Add(window);
            window.Show(ChoicePage("question", "yes", "no"), new MessageWindow.Page(null, "second"), new MessageWindow.Page(null, "third"));
            yield return Settle();

            Press(ChoicesOf(window)[0]);
            yield return Settle();

            CollectionAssert.AreEqual(new[] { 0 }, selected);
            Assert.AreEqual(1, window.pageIndex);
            Assert.AreEqual(0, ChoicesOf(window).Count);
        }

        // (D) 最後のページの選択肢を押すと、choiceSelected の後に completed が 1 回出る。選択肢は消え、その後の Submit で再び出ない
        [UnityTest]
        public IEnumerator Choice_OnLastPage_FiresChoiceSelectedThenCompleted()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            var events = new List<string>();
            window.choiceSelected += index => events.Add("choice " + index);
            window.completed += () => events.Add("completed");
            root.Add(window);
            window.Show(ChoicePage("question", "yes", "no"));
            yield return Settle();

            SubmitTo(ChoicesOf(window)[0]);
            yield return Settle();
            Submit(window);
            yield return Settle();

            CollectionAssert.AreEqual(new[] { "choice 0", "completed" }, events);
            Assert.AreEqual(0, window.pageIndex);
            Assert.IsFalse(window.ClassListContains("is-choosing"));
            Assert.AreEqual(0, ChoicesOf(window).Count);
        }

        // (D) choiceSelected の中で Show すると、部品は進まず、新しい 1 ページ目が出る（分岐は利用者が Show し直して行う）
        [UnityTest]
        public IEnumerator Choice_ShowInChoiceSelected_StartsNewPagesWithoutAdvancing()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            var completed = 0;
            window.completed += () => completed++;
            window.choiceSelected += index => window.Show("branch one", "branch two");
            root.Add(window);
            window.Show(ChoicePage("question", "yes", "no"));
            yield return Settle();

            SubmitTo(ChoicesOf(window)[1]);
            yield return Settle();

            Assert.AreEqual(0, window.pageIndex);
            Assert.AreEqual(2, window.pageCount);
            Assert.AreEqual("branch one", TextOf(window).parsedText);
            Assert.AreEqual(0, completed);
            Assert.IsFalse(window.ClassListContains("is-choosing"));
            Assert.IsTrue(window.ClassListContains("is-waiting"));
        }

        // (E) 窓にフォーカスがある状態で選択肢が出ると、最初の選択肢にフォーカスが移る。即時表示と、文字送りの終わり（Submit で全文を出す）の両方
        [UnityTest]
        public IEnumerator Choices_WindowFocused_FocusMovesToFirstChoice([Values(0f, 10f)] float charactersPerSecond)
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow { charactersPerSecond = charactersPerSecond };
            root.Add(window);
            window.Focus();
            yield return null;
            Assert.AreEqual(window, window.focusController.focusedElement, "precondition: the window has focus");

            window.Show(ChoicePage("01234567890123456789", "yes", "no"));
            yield return Settle();
            if (charactersPerSecond > 0)
            {
                Assert.IsTrue(window.ClassListContains("is-revealing"), "the reveal ended before Submit");
                Submit(window);
                yield return Settle();
            }

            Assert.AreEqual(ChoicesOf(window)[0], window.focusController.focusedElement);
        }

        // (E) 窓にフォーカスが無ければ、選択肢が出てもフォーカスを奪わない
        [UnityTest]
        public IEnumerator Choices_WindowNotFocused_DoesNotTakeFocus()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var other = new Button { text = "other" };
            root.Add(other);
            var window = new MessageWindow();
            root.Add(window);
            other.Focus();
            yield return null;

            window.Show(ChoicePage("question", "yes", "no"));
            yield return Settle();

            Assert.AreEqual(other, window.focusController.focusedElement);
        }

        // (E) 選択肢を Submit で選んだ後は、フォーカスが外れた Button に残らず、次のページの窓（次も選択肢なら最初の選択肢）へ移る
        [UnityTest]
        public IEnumerator Choice_Selected_FocusReturnsToWindowOrNextChoices()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);
            window.Focus();
            yield return null;
            window.Show(ChoicePage("first", "a", "b"), ChoicePage("second", "c", "d"), new MessageWindow.Page(null, "third"));
            yield return Settle();
            Assert.AreEqual(ChoicesOf(window)[0], window.focusController.focusedElement, "precondition: the first choice has focus");

            SubmitTo(ChoicesOf(window)[0]);
            yield return Settle();
            Assert.AreEqual(1, window.pageIndex);
            Assert.AreEqual(ChoicesOf(window)[0], window.focusController.focusedElement, "next choices");

            SubmitTo(ChoicesOf(window)[1]);
            yield return Settle();
            Assert.AreEqual(2, window.pageIndex);
            Assert.AreEqual(window, window.focusController.focusedElement, "the window on a page without choices");
        }

        // (F) 選択肢の無いページでは、選択肢の並びは空で高さ 0、is-choosing は付かない
        [UnityTest]
        public IEnumerator Show_PageWithoutChoices_HasEmptyChoices()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);

            window.Show("first", "second");
            yield return Settle();

            Assert.AreEqual(0, ChoicesOf(window).Count);
            Assert.AreEqual(0f, window.Q(className: "hone-message-window__choices").layout.height);
            Assert.IsFalse(window.ClassListContains("is-choosing"));
        }

        // 窓にフォーカスを置いて 1 frame 待ってから、選択肢のページを出す
        IEnumerator ShowFocused(MessageWindow window, params MessageWindow.Page[] pages)
        {
            window.Focus();
            yield return null;
            window.Show(pages);
            yield return Settle();
        }

        // (D) choiceSelected の中で Advance() しても、部品はそれ以上進めない（1 ページだけ進む）
        [UnityTest]
        public IEnumerator Choice_AdvanceInChoiceSelected_AdvancesOnce()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            var completed = 0;
            window.completed += () => completed++;
            window.choiceSelected += index => window.Advance();
            root.Add(window);
            window.Show(ChoicePage("question", "yes", "no"), new MessageWindow.Page(null, "second"), new MessageWindow.Page(null, "third"));
            yield return Settle();

            SubmitTo(ChoicesOf(window)[0]);
            yield return Settle();

            Assert.AreEqual(1, window.pageIndex);
            Assert.AreEqual(0, completed);
        }

        // (B) 時間で文字送りが終わっても、選択肢が出て、窓にフォーカスがあれば最初の選択肢へ移る
        [UnityTest]
        public IEnumerator Reveal_FinishesByTime_ShowsChoicesAndMovesFocus()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow { charactersPerSecond = 100 };
            root.Add(window);

            yield return ShowFocused(window, ChoicePage("0123456789", "yes", "no"));
            yield return WaitRealtime(0.5f);
            yield return Settle();

            Assert.IsFalse(window.ClassListContains("is-revealing"));
            Assert.IsTrue(window.ClassListContains("is-choosing"));
            Assert.AreEqual(2, ChoicesOf(window).Count);
            Assert.AreEqual(ChoicesOf(window)[0], window.focusController.focusedElement);
        }

        // 選択肢を出している間に Show すると、選択肢が消えて is-choosing が外れ、新しいページを Submit で送れる。ページ無しにしても外れる
        [UnityTest]
        public IEnumerator Choosing_ShowFromOutside_ClearsChoices()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);
            window.Show(ChoicePage("question", "yes", "no"));
            yield return Settle();

            window.Show("next", "last");
            yield return Settle();
            Assert.AreEqual(0, ChoicesOf(window).Count);
            Assert.IsFalse(window.ClassListContains("is-choosing"));
            Submit(window);
            Assert.AreEqual(1, window.pageIndex);

            window.Show(ChoicePage("question", "yes", "no"));
            yield return Settle();
            window.Show((MessageWindow.Page[])null);
            yield return Settle();
            Assert.AreEqual(0, ChoicesOf(window).Count);
            Assert.IsFalse(window.ClassListContains("is-choosing"));
        }

        // (E) choiceSelected の中で Show し直すと、フォーカスは新しいページへ移る（選択肢のページなら最初の選択肢、そうでなければ窓）
        [UnityTest]
        public IEnumerator Choice_ShowInChoiceSelected_MovesFocusToNewPage([Values(true, false)] bool nextHasChoices)
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            window.choiceSelected += index =>
            {
                if (nextHasChoices)
                    window.Show(ChoicePage("branch", "c", "d"));
                else
                    window.Show("branch");
            };
            root.Add(window);
            yield return ShowFocused(window, ChoicePage("question", "yes", "no"));

            SubmitTo(ChoicesOf(window)[0]);
            yield return Settle();

            var expected = nextHasChoices ? ChoicesOf(window)[0] : (Focusable)window;
            Assert.AreEqual(expected, window.focusController.focusedElement);
        }

        // (E) choiceSelected か completed の中で利用者が移したフォーカスは、部品が上書きしない
        [UnityTest]
        public IEnumerator Choice_HandlerMovesFocus_IsKept([Values(true, false)] bool inCompleted)
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var other = new Button { text = "other" };
            root.Add(other);
            var window = new MessageWindow();
            if (inCompleted)
                window.completed += () => other.Focus();
            else
                window.choiceSelected += index => { window.Show("branch"); other.Focus(); };
            root.Add(window);
            yield return ShowFocused(window, ChoicePage("question", "yes", "no"));

            SubmitTo(ChoicesOf(window)[0]);
            yield return Settle();

            Assert.AreEqual(other, window.focusController.focusedElement);
        }

        // (E) 最後のページで選んで completed が出ても、利用者がフォーカスを移さなければ、外した Button に残さず窓へ移す
        [UnityTest]
        public IEnumerator Choice_OnLastPage_FocusReturnsToWindow()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);
            yield return ShowFocused(window, ChoicePage("question", "yes", "no"));

            SubmitTo(ChoicesOf(window)[1]);
            yield return Settle();

            Assert.AreEqual(window, window.focusController.focusedElement);
        }

        // (E) 窓に Focus() したのと同じ frame で選択肢のページを出しても、最初の選択肢にフォーカスが移る
        [UnityTest]
        public IEnumerator Choices_FocusAndShowInSameFrame_FocusMovesToFirstChoice()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);

            window.Focus();
            window.Show(ChoicePage("question", "yes", "no"));
            yield return Settle();

            Assert.AreEqual(ChoicesOf(window)[0], window.focusController.focusedElement);
        }

        // choices の null の要素は、空文字列の選択肢として出る
        [UnityTest]
        public IEnumerator Show_NullChoice_ShowsEmptyText()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow();
            root.Add(window);

            window.Show(ChoicePage("question", null, "no"));
            yield return Settle();

            Assert.AreEqual("", ChoicesOf(window)[0].text);
            Assert.AreEqual("no", ChoicesOf(window)[1].text);
        }

        // 選択肢のページの文字送りの途中で panel から外すと、全文が出て選択肢も出た状態になる（付け直しても再開しない）
        [UnityTest]
        public IEnumerator Detach_WhileRevealingChoicePage_ShowsChoices()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var window = new MessageWindow { charactersPerSecond = 10 };
            root.Add(window);
            yield return ShowFocused(window, ChoicePage("01234567890123456789", "yes", "no"));
            Assert.IsTrue(window.ClassListContains("is-revealing"), "the reveal ended before detaching");

            window.RemoveFromHierarchy();
            root.Add(window);
            yield return Settle();

            Assert.IsFalse(window.ClassListContains("is-revealing"));
            Assert.IsTrue(window.ClassListContains("is-choosing"));
            Assert.AreEqual(2, ChoicesOf(window).Count);
        }
    }
}
#endif
