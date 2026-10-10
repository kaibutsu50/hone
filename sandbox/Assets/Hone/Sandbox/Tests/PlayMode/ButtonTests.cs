#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hone.Sandbox.Tests
{
    // Editor で実行する前提（UXML と PanelSettings を AssetDatabase から読む）。
    // このファイルの Button は Hone.Button を指す（Hone の入れ子の namespace なので、using UnityEngine.UIElements; があっても Hone.Button が勝つ）。
    public class ButtonTests
    {
        const string ButtonUxmlPath = "Assets/Hone/UI/Button/Button.uxml";
        const string PanelSettingsPath = "Assets/Hone/Sandbox/PanelSettings.asset";
        const string SandboxUxmlPath = "Assets/Hone/Sandbox/Sandbox.uxml";
        const int MaxWaitFrames = 60;

        readonly List<Object> m_Created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in m_Created)
                Object.Destroy(obj);
            m_Created.Clear();
        }

        static List<string> VariantClasses(VisualElement element) =>
            element.GetClasses().Where(c => c.StartsWith("hone-button--")).ToList();

        // (A) UXML の variant="outline" が hone-button と hone-button--outline のクラスになる
        [Test]
        public void Uxml_OutlineVariant_HasBlockAndVariantClass()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ButtonUxmlPath);
            Assert.IsNotNull(tree, $"{ButtonUxmlPath} was not found");

            var root = tree.Instantiate();
            var outline = root.Q<Button>(className: "hone-button--outline");

            Assert.IsNotNull(outline, "no Hone.Button with hone-button--outline");
            Assert.IsTrue(outline.ClassListContains("hone-button"));
            Assert.AreEqual(Button.Variant.Outline, outline.variant);
            CollectionAssert.AreEqual(new[] { "hone-button--outline" }, VariantClasses(outline));
        }

        // (B) variant を変えると、前の variant のクラスが外れて新しいクラスが付く。Default はクラスなし
        [Test]
        public void Variant_Change_ReplacesVariantClass()
        {
            var button = new Button { variant = Button.Variant.Outline };
            CollectionAssert.AreEqual(new[] { "hone-button--outline" }, VariantClasses(button));

            button.variant = Button.Variant.Ghost;
            CollectionAssert.AreEqual(new[] { "hone-button--ghost" }, VariantClasses(button));

            button.variant = Button.Variant.Default;
            CollectionAssert.IsEmpty(VariantClasses(button));
            Assert.IsTrue(button.ClassListContains("hone-button"), "the block class must stay");
        }

        // (C) 既定テーマの見た目（unity-button）が付いていない。基底の Button が付けるクラスを外してある
        [Test]
        public void Constructor_HasNoUnityButtonClass()
        {
            var button = new Button();

            Assert.IsFalse(button.ClassListContains("unity-button"));
            Assert.IsTrue(button.ClassListContains("hone-button"));
            Assert.IsTrue(button.ClassListContains("hone-focusable"));
            Assert.IsTrue(button.ClassListContains("hone-text"));
        }

        // panel に付いた要素でないと SendEvent が届かないので、PanelRenderer を 1 つ作り、panel に付いた root を onReady に渡す
        IEnumerator CreatePanel(System.Action<VisualElement> onReady)
        {
            var panelSettings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath));
            m_Created.Add(panelSettings);
            var go = new GameObject("ButtonTests");
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

        static void Submit(VisualElement target)
        {
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        // (D) 基底の挙動が残っている。NavigationSubmitEvent で clicked が 1 回発火する
        [UnityTest]
        public IEnumerator Clicked_FiresOnNavigationSubmit()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var button = new Button { text = "submit" };
            var clicks = 0;
            button.clicked += () => clicks++;
            root.Add(button);
            yield return null;

            Submit(button);
            yield return null;

            Assert.AreEqual(1, clicks);
        }

        // (Hold A) commitDelay が 0 のとき、Submit で committed が 1 回出て、is-holding は付かない
        [UnityTest]
        public IEnumerator Committed_FiresImmediately_WhenDelayIsZero()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var button = new Button();
            var commits = 0;
            button.committed += () => commits++;
            root.Add(button);
            yield return null;

            Submit(button);

            Assert.AreEqual(1, commits);
            Assert.IsFalse(button.isHolding);
            Assert.IsFalse(button.ClassListContains("is-holding"));
        }

        // commitDelay が負のときは 0 と同じく、Submit の処理の中で committed が出る
        [UnityTest]
        public IEnumerator Committed_FiresImmediately_WhenDelayIsNegative()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var button = new Button { commitDelay = -1f };
            var commits = 0;
            button.committed += () => commits++;
            root.Add(button);
            yield return null;

            Submit(button);

            Assert.AreEqual(1, commits);
            Assert.IsFalse(button.isHolding);
        }

        // (Hold B) commitDelay が 0.2 のとき、直後は Hold 中で committed は 0 回。待つと 1 回出て Hold が終わる
        [UnityTest]
        public IEnumerator Committed_FiresAfterDelay_AndHoldingClassToggles()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var button = new Button { commitDelay = 0.2f };
            var commits = 0;
            button.committed += () => commits++;
            root.Add(button);
            yield return null;

            Submit(button);

            Assert.IsTrue(button.isHolding);
            Assert.IsTrue(button.ClassListContains("is-holding"));
            Assert.AreEqual(0, commits);

            yield return new WaitForSeconds(0.4f);

            Assert.AreEqual(1, commits);
            Assert.IsFalse(button.isHolding);
            Assert.IsFalse(button.ClassListContains("is-holding"));
        }

        // (Hold C) Hold の間にもう一度押すと、clicked は 2 回出るが、committed は最終的に 1 回
        [UnityTest]
        public IEnumerator Press_DuringHold_FiresClickedButCommitsOnce()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var button = new Button { commitDelay = 0.5f };
            var clicks = 0;
            var commits = 0;
            button.clicked += () => clicks++;
            button.committed += () => commits++;
            root.Add(button);
            yield return null;

            Submit(button);
            yield return null;
            Submit(button);

            Assert.AreEqual(2, clicks);
            Assert.IsTrue(button.isHolding);
            Assert.AreEqual(0, commits);

            yield return new WaitForSeconds(0.8f);

            Assert.AreEqual(1, commits);
            Assert.IsFalse(button.isHolding);
        }

        // (Hold D) Hold の途中で panel から外すと、is-holding が外れ、待っても committed は出ない。
        // 外したままだと予約は panel に付いていないので動かない。付け直してから待ち、予約が残っていないことを確かめる
        [UnityTest]
        public IEnumerator Detach_DuringHold_CancelsCommit()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var button = new Button { commitDelay = 0.2f };
            var commits = 0;
            button.committed += () => commits++;
            root.Add(button);
            yield return null;

            Submit(button);
            Assert.IsTrue(button.isHolding);

            button.RemoveFromHierarchy();

            Assert.IsFalse(button.isHolding);
            Assert.IsFalse(button.ClassListContains("is-holding"));

            root.Add(button);
            yield return new WaitForSeconds(0.4f);

            Assert.AreEqual(0, commits);

            // 付け直した後の押下では、新しい Hold が始まる
            Submit(button);
            Assert.IsTrue(button.isHolding);
        }

        // Hold の途中で commitDelay を変えても、進行中の Hold は押した時点の値で確定する
        [UnityTest]
        public IEnumerator CommitDelay_ChangedDuringHold_DoesNotAffectCurrentHold()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var button = new Button { commitDelay = 0.2f };
            var commits = 0;
            button.committed += () => commits++;
            root.Add(button);
            yield return null;

            Submit(button);
            button.commitDelay = 5f;

            yield return new WaitForSeconds(0.4f);

            Assert.AreEqual(1, commits);
            Assert.IsFalse(button.isHolding);
        }

        // Hold が終わった後にもう一度押すと、2 回目の Hold が始まり、committed は合計 2 回になる
        [UnityTest]
        public IEnumerator Press_AfterHold_StartsNewHold()
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);

            var button = new Button { commitDelay = 0.2f };
            var commits = 0;
            button.committed += () => commits++;
            root.Add(button);
            yield return null;

            Submit(button);
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(1, commits);

            Submit(button);
            Assert.IsTrue(button.isHolding);

            yield return new WaitForSeconds(0.4f);

            Assert.AreEqual(2, commits);
            Assert.IsFalse(button.isHolding);
        }

        // (Hold E) UXML の commit-delay="0.5" が commitDelay 0.5 になる。
        // Button.uxml の使用例のうち、destructive の例に commit-delay="0.5" を付けてあるのを読む
        [Test]
        public void Uxml_CommitDelay_IsParsed()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ButtonUxmlPath);
            Assert.IsNotNull(tree, $"{ButtonUxmlPath} was not found");

            var root = tree.Instantiate();
            var delayed = root.Q<Button>(className: "hone-button--destructive");

            Assert.IsNotNull(delayed, "no Hone.Button with hone-button--destructive");
            Assert.AreEqual(0.5f, delayed.commitDelay);
        }
    }
}
#endif
