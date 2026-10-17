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
    // Spinner は .cs を持たない見た目だけの部品なので、確かめるのは、USS が theme を通して当たっていること（resolvedStyle がトークンの値になること）。
    // 回転は利用者が注入する演出なので確かめない。
    // 期待値は Tokens.uss の値を書き写したもの。
    // コメントの (C) は Issue #129 の受け入れ条件の番号
    public class SpinnerTests
    {
        const string UxmlPath = "Assets/Hone/UI/Spinner/Spinner.uxml";
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
            var go = new GameObject("SpinnerTests");
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

        // Spinner.uxml を Instantiate して panel に置き、輪がレイアウトされるまで待つ
        IEnumerator PlaceSpinner(System.Action<VisualElement> onReady)
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(tree, $"{UxmlPath} was not found");
            var container = tree.Instantiate();
            root.Add(container);
            var spinner = container.Q(className: "hone-spinner");
            Assert.IsNotNull(spinner, "Spinner.uxml must have a .hone-spinner");
            var frames = 0;
            while (frames < MaxWaitFrames && !(spinner.layout.width > 0f))
            {
                frames++;
                yield return null;
            }
            Assert.Greater(spinner.layout.width, 0f, "the spinner was not laid out");
            onReady(spinner);
        }

        // (C) Spinner.uxml の .hone-spinner の大きさが --hone-size-spinner（16px）、角丸が --hone-radius-spinner（8px）で円になる。
        // 枠の太さは --hone-border-width（1px）、色は上の 1 辺だけ透明で、残りの 3 辺が --hone-color-foreground。
        // flex-shrink は 0
        [UnityTest]
        public IEnumerator Ring_ResolvesSizeAndColorFromTokens()
        {
            VisualElement spinner = null;
            yield return PlaceSpinner(s => spinner = s);

            var style = spinner.resolvedStyle;
            Assert.AreEqual(16f, style.width);
            Assert.AreEqual(16f, style.height);
            Assert.AreEqual(0f, style.flexShrink);
            Assert.AreEqual(8f, style.borderTopLeftRadius);
            Assert.AreEqual(8f, style.borderTopRightRadius);
            Assert.AreEqual(8f, style.borderBottomLeftRadius);
            Assert.AreEqual(8f, style.borderBottomRightRadius);
            Assert.AreEqual(1f, style.borderTopWidth);
            Assert.AreEqual(1f, style.borderRightWidth);
            Assert.AreEqual(1f, style.borderBottomWidth);
            Assert.AreEqual(1f, style.borderLeftWidth);
            var foreground = new Color32(0x09, 0x09, 0x0b, 0xff);
            Assert.AreEqual(0f, style.borderTopColor.a, "the top side must be transparent");
            Assert.AreEqual(foreground, (Color32)style.borderRightColor);
            Assert.AreEqual(foreground, (Color32)style.borderBottomColor);
            Assert.AreEqual(foreground, (Color32)style.borderLeftColor);
        }

        // (C) 使用例の行（輪と Label の横並び）の幅を、輪と文言が収まらない 40px に絞っても、輪は 16px の円のまま潰れない
        [UnityTest]
        public IEnumerator Ring_DoesNotShrinkInNarrowRow()
        {
            VisualElement spinner = null;
            yield return PlaceSpinner(s => spinner = s);
            var row = spinner.parent;
            row.style.width = 40;
            row.Q<Label>().text = "Loading a long list of items...";
            var frames = 0;
            while (frames < MaxWaitFrames && !Mathf.Approximately(row.layout.width, 40f))
            {
                frames++;
                yield return null;
            }
            yield return null;

            Assert.AreEqual(40f, row.layout.width, 0.01f);
            Assert.AreEqual(16f, spinner.layout.width, 0.01f);
            Assert.AreEqual(16f, spinner.layout.height, 0.01f);
        }
    }
}
#endif
