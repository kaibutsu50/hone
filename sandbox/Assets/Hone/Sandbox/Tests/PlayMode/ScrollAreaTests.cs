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
    // ScrollArea は .cs を持たない見た目だけの部品なので、確かめるのは USS が theme を通して当たっていること（resolvedStyle がトークンの値になること）と、
    // スクロールの標準の振る舞い（つまみの大きさ、scrollOffset で中身が動くこと）を壊していないことだけ。
    // 期待値は Tokens.uss の値を書き写したもの。
    // コメントの (B) (C) は Issue #125 の受け入れ条件の番号
    public class ScrollAreaTests
    {
        const string UxmlPath = "Assets/Hone/UI/ScrollArea/ScrollArea.uxml";
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
            var go = new GameObject("ScrollAreaTests");
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

        // ScrollArea.uxml を Instantiate して panel に置き、中身が収まらない高さ（64px）に固定して、つまみがレイアウトされるまで待つ。
        // vertical-scroller-visibility は Auto なので、中身が収まるとスクロールバーが出ない。使用例の高さ（96px）に依らず必ず溢れるようにする
        IEnumerator PlaceScrollArea(System.Action<ScrollView> onReady)
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(tree, $"{UxmlPath} was not found");
            var container = tree.Instantiate();
            root.Add(container);
            var scrollView = container.Q<ScrollView>(className: "hone-scroll-area");
            Assert.IsNotNull(scrollView);
            scrollView.parent.style.height = 64;
            var dragger = scrollView.verticalScroller.slider.Q(className: BaseSlider<float>.draggerUssClassName);
            Assert.IsNotNull(dragger);
            var frames = 0;
            while (frames < MaxWaitFrames && !(dragger.layout.height > 0))
            {
                frames++;
                yield return null;
            }
            Assert.Greater(dragger.layout.height, 0f, "the dragger was not laid out");
            onReady(scrollView);
        }

        // (B) 縦のスクロールバーの幅が 10、背景が透明、枠が無い。上下の矢印ボタンは表示されない
        [UnityTest]
        public IEnumerator ScrollArea_ScrollerIsTokenWidthWithoutArrowButtons()
        {
            ScrollView scrollView = null;
            yield return PlaceScrollArea(s => scrollView = s);

            var scroller = scrollView.verticalScroller;
            Assert.IsTrue(scroller.ClassListContains(Scroller.verticalVariantUssClassName));
            var style = scroller.resolvedStyle;
            Assert.AreEqual(10f, style.width);
            Assert.AreEqual(0f, style.backgroundColor.a);
            Assert.AreEqual(0f, style.borderLeftWidth);
            Assert.AreEqual(DisplayStyle.None, scroller.lowButton.resolvedStyle.display);
            Assert.AreEqual(DisplayStyle.None, scroller.highButton.resolvedStyle.display);
        }

        // (B) つまみの背景色が #e4e4e7（--hone-color-border）、角丸が 5、枠が無く、幅がスクロールバーいっぱい（既定テーマの 18px ではない）。スライダーは矢印ボタンの分の余白を持たず、スクロールバーと同じ大きさ。溝は背景が透明で枠が無い。つまみの枠は表示されない
        [UnityTest]
        public IEnumerator ScrollArea_DraggerFollowsBorderColorAndScrollbarRadius()
        {
            ScrollView scrollView = null;
            yield return PlaceScrollArea(s => scrollView = s);

            var slider = scrollView.verticalScroller.slider;
            var dragger = slider.Q(className: BaseSlider<float>.draggerUssClassName);
            var tracker = slider.Q(className: BaseSlider<float>.trackerUssClassName);
            var draggerBorder = slider.Q(className: BaseSlider<float>.draggerBorderUssClassName);
            Assert.IsNotNull(tracker);
            Assert.IsNotNull(draggerBorder);

            var style = dragger.resolvedStyle;
            Assert.AreEqual(new Color32(0xe4, 0xe4, 0xe7, 0xff), (Color32)style.backgroundColor);
            Assert.AreEqual(5f, style.borderTopLeftRadius);
            Assert.AreEqual(5f, style.borderBottomRightRadius);
            Assert.AreEqual(0f, style.borderLeftWidth);
            Assert.AreEqual(0f, style.borderTopWidth);
            Assert.AreEqual(10f, dragger.layout.width);
            Assert.AreEqual(0f, slider.resolvedStyle.marginTop);
            Assert.AreEqual(0f, slider.resolvedStyle.marginBottom);
            Assert.AreEqual(10f, slider.layout.width);
            Assert.AreEqual(scrollView.verticalScroller.layout.height, slider.layout.height);
            Assert.AreEqual(0f, tracker.resolvedStyle.backgroundColor.a);
            Assert.AreEqual(0f, tracker.resolvedStyle.borderLeftWidth);
            Assert.AreEqual(DisplayStyle.None, draggerBorder.resolvedStyle.display);
        }

        // (C) 中身が収まらないとき、つまみの高さが 0 より大きくスクロールバーの高さより小さい。scrollOffset を変えると中身が動く
        [UnityTest]
        public IEnumerator ScrollArea_DraggerIsSmallerThanScrollerAndScrollOffsetMovesContent()
        {
            ScrollView scrollView = null;
            yield return PlaceScrollArea(s => scrollView = s);

            var scroller = scrollView.verticalScroller;
            var dragger = scroller.slider.Q(className: BaseSlider<float>.draggerUssClassName);
            Assert.Greater(dragger.layout.height, 0f);
            Assert.Less(dragger.layout.height, scroller.layout.height);

            var before = scrollView.contentContainer.worldBound.yMin;
            scrollView.scrollOffset = new Vector2(0f, 20f);
            yield return null;
            Assert.AreEqual(20f, scrollView.scrollOffset.y);
            Assert.Less(scrollView.contentContainer.worldBound.yMin, before);
        }
    }
}
#endif
