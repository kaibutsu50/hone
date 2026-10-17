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
    // Editor で実行する前提（UXML と PanelSettings を AssetDatabase から読む）。
    // Card は .cs を持たない見た目だけの部品なので、確かめるのは USS が theme を通して当たっていること（resolvedStyle がトークンの値になること）だけ。
    // 期待値は Tokens.uss の値を書き写したもの（枠 1px、border #e4e4e7、background #ffffff、radius-lg 8px、font-size 2xl 24px / xl 20px / sm 14px、space 24px / 16px）。
    // コメントの (C) は Issue #114 の受け入れ条件の番号
    public class CardTests
    {
        const string UxmlPath = "Assets/Hone/UI/Card/Card.uxml";
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

        // PanelRenderer を 1 つ作り、UI が読み込まれたら root を返す。PanelSettings は複製して、panel の状態をテスト間で共有しない
        IEnumerator CreatePanel(System.Action<VisualElement> onReady)
        {
            var source = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            Assert.IsNotNull(source, $"{PanelSettingsPath} was not found");
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SandboxUxmlPath);
            Assert.IsNotNull(uxml, $"{SandboxUxmlPath} was not found");
            var panelSettings = Object.Instantiate(source);
            // 画面の大きさに応じた拡大（ScaleWithScreenSize）がかかると、px の期待値と resolvedStyle がずれる。画面の大きさに依らないよう、等倍に固定する
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            panelSettings.scale = 1f;
            m_Created.Add(panelSettings);
            var go = new GameObject("CardTests");
            m_Created.Add(go);
            go.SetActive(false);
            var panelRenderer = go.AddComponent<PanelRenderer>();
            panelRenderer.panelSettings = panelSettings;
            panelRenderer.visualTreeAsset = uxml;
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

        // Card.uxml を 2 回読み、2 枚目に hone-card--compact を付けて（Default と Compact の 2 枚）panel に置き、レイアウトされるまで待つ
        IEnumerator PlaceCards(System.Action<VisualElement, VisualElement> onReady)
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(tree, $"{UxmlPath} was not found");
            var cards = new List<VisualElement>();
            for (var i = 0; i < 2; i++)
            {
                var container = tree.Instantiate();
                root.Add(container);
                var card = container.Q(className: "hone-card");
                Assert.IsNotNull(card);
                cards.Add(card);
            }
            cards[1].AddToClassList("hone-card--compact");
            var frames = 0;
            while (frames < MaxWaitFrames && cards.Exists(c => !(c.worldBound.height > 0)))
            {
                frames++;
                yield return null;
            }
            Assert.Greater(cards[0].worldBound.height, 0f, "the card was not laid out");
            onReady(cards[0], cards[1]);
        }

        // (C) .hone-card の枠の太さ・枠色・背景色・角丸が、対応するトークンの値になる
        [UnityTest]
        public IEnumerator Card_ResolvesBorderBackgroundAndRadiusFromTokens()
        {
            VisualElement card = null;
            yield return PlaceCards((d, c) => card = d);

            var style = card.resolvedStyle;
            Assert.AreEqual(1f, style.borderTopWidth);
            Assert.AreEqual(1f, style.borderLeftWidth);
            Assert.AreEqual(new Color32(0xe4, 0xe4, 0xe7, 0xff), (Color32)style.borderTopColor);
            Assert.AreEqual(new Color32(0xff, 0xff, 0xff, 0xff), (Color32)style.backgroundColor);
            Assert.AreEqual(8f, style.borderTopLeftRadius);
            Assert.AreEqual(8f, style.borderBottomRightRadius);
        }

        // (C) .hone-card__title の fontSize が 24px、--compact のとき 20px。description は 14px
        [UnityTest]
        public IEnumerator Card_TitleFontSizeIs24AndCompactIs20()
        {
            VisualElement card = null;
            VisualElement compact = null;
            yield return PlaceCards((d, c) => { card = d; compact = c; });

            Assert.AreEqual(24f, card.Q<Label>(className: "hone-card__title").resolvedStyle.fontSize);
            Assert.AreEqual(14f, card.Q<Label>(className: "hone-card__description").resolvedStyle.fontSize);
            Assert.IsTrue(compact.ClassListContains("hone-card--compact"));
            Assert.AreEqual(20f, compact.Q<Label>(className: "hone-card__title").resolvedStyle.fontSize);
        }

        // header は上下左右、content と footer は上の余白を持たない。--compact は 24px が 16px になる
        [UnityTest]
        public IEnumerator Card_PaddingFollowsSpaceTokens()
        {
            VisualElement card = null;
            VisualElement compact = null;
            yield return PlaceCards((d, c) => { card = d; compact = c; });

            var header = card.Q(className: "hone-card__header").resolvedStyle;
            Assert.AreEqual(24f, header.paddingTop);
            Assert.AreEqual(24f, header.paddingLeft);
            var content = card.Q(className: "hone-card__content").resolvedStyle;
            Assert.AreEqual(0f, content.paddingTop);
            Assert.AreEqual(24f, content.paddingBottom);
            Assert.AreEqual(FlexDirection.Row, card.Q(className: "hone-card__footer").resolvedStyle.flexDirection);

            var compactHeader = compact.Q(className: "hone-card__header").resolvedStyle;
            Assert.AreEqual(16f, compactHeader.paddingTop);
            var compactContent = compact.Q(className: "hone-card__content").resolvedStyle;
            Assert.AreEqual(0f, compactContent.paddingTop);
            Assert.AreEqual(16f, compactContent.paddingLeft);
            Assert.AreEqual(16f, compactContent.paddingBottom);
        }
    }
}
#endif
