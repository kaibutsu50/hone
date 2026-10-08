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
    // parsedText はレイアウトの後に埋まるので、Show / Advance の後は frame を進めてから読む
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

        static void Click(MessageWindow window)
        {
            using (var evt = ClickEvent.GetPooled())
            {
                evt.target = window;
                window.SendEvent(evt);
            }
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
            yield return null;

            Assert.IsTrue(window.ClassListContains("is-revealing"));
            Assert.IsFalse(window.ClassListContains("is-waiting"));
            StringAssert.Contains("<alpha=#00>", TextOf(window).text, "the unrevealed part must be hidden");

            Submit(window);
            yield return Settle();

            Assert.AreEqual(0, window.pageIndex);
            Assert.IsFalse(window.ClassListContains("is-revealing"));
            Assert.IsTrue(window.ClassListContains("is-waiting"));
            Assert.AreEqual(page, TextOf(window).parsedText);
            StringAssert.DoesNotContain("<alpha", TextOf(window).text, "nothing may stay hidden once the full text is shown");
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
            Assert.Greater(revealingHeight, 0f);

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
    }
}
#endif
