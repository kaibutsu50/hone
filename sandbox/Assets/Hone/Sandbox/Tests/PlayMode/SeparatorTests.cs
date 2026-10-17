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
    // Separator は .cs を持たない見た目だけの部品なので、確かめるのは、USS が theme を通して当たっていること（resolvedStyle がトークンの値になること）と、親の幅（高さ）いっぱいに伸びること。
    // 期待値は Tokens.uss の値を書き写したもの。
    // コメントの (B) は Issue #115 の受け入れ条件の番号
    public class SeparatorTests
    {
        const string UxmlPath = "Assets/Hone/UI/Separator/Separator.uxml";
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
            var go = new GameObject("SeparatorTests");
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

        // Separator.uxml を Instantiate して panel に置き、レイアウトされるまで待つ。
        // 横線の親（縦並びの枠）は幅を 200px に固定し、縦線の親（横並びの行）の高さは中の Label で決まる。
        // 2 つの親は align-items を flex-start にして、線が親の既定の stretch ではなく自分の align-self: stretch で伸びることを確かめる
        IEnumerator PlaceSeparators(System.Action<VisualElement, VisualElement, VisualElement> onReady)
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(tree, $"{UxmlPath} was not found");
            var container = tree.Instantiate();
            root.Add(container);
            var column = container[0];
            column.style.width = 200;
            var horizontal = column.Query(className: "hone-separator").Where(e => !e.ClassListContains("hone-separator--vertical")).First();
            var vertical = column.Q(className: "hone-separator--vertical");
            Assert.IsNotNull(horizontal);
            Assert.IsNotNull(vertical);
            var row = vertical.parent;
            column.style.alignItems = Align.FlexStart;
            row.style.alignItems = Align.FlexStart;
            var frames = 0;
            while (frames < MaxWaitFrames && !(row.worldBound.height > 0f))
            {
                frames++;
                yield return null;
            }
            Assert.Greater(row.worldBound.height, 0f, "the separators were not laid out");
            onReady(column, horizontal, vertical);
        }

        // (B) 横線の高さが --hone-border-width（1px）、背景色が --hone-color-border。縦並びの親の幅いっぱいに伸びる。flex-shrink は 0
        [UnityTest]
        public IEnumerator Horizontal_ResolvesHeightAndColorFromTokensAndFillsParentWidth()
        {
            VisualElement column = null;
            VisualElement horizontal = null;
            yield return PlaceSeparators((c, h, v) => { column = c; horizontal = h; });

            var style = horizontal.resolvedStyle;
            Assert.AreEqual(1f, style.height);
            Assert.AreEqual(new Color32(0xe4, 0xe4, 0xe7, 0xff), (Color32)style.backgroundColor);
            Assert.AreEqual(column.resolvedStyle.width, style.width);
            Assert.AreEqual(0f, style.flexShrink);
        }

        // (B) 縦線の幅が --hone-border-width（1px）、背景色が --hone-color-border。横並びの親の高さいっぱいに伸びる。flex-shrink は 0
        [UnityTest]
        public IEnumerator Vertical_ResolvesWidthAndColorFromTokensAndFillsParentHeight()
        {
            VisualElement vertical = null;
            yield return PlaceSeparators((c, h, v) => vertical = v);

            var style = vertical.resolvedStyle;
            var row = vertical.parent;
            Assert.AreEqual(1f, style.width);
            Assert.AreEqual(new Color32(0xe4, 0xe4, 0xe7, 0xff), (Color32)style.backgroundColor);
            Assert.Greater(row.resolvedStyle.height, 1f);
            Assert.AreEqual(row.resolvedStyle.height, style.height);
            Assert.AreEqual(0f, style.flexShrink);
        }
    }
}
#endif
