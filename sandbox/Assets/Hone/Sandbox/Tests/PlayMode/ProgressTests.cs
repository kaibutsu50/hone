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
    // UnityEditor.Progress と名前が重なるので、Hone.Progress は完全修飾で書く。
    // 期待値は Tokens.uss の値を書き写したもの。
    // コメントの (B) は Issue #129 の受け入れ条件の番号
    public class ProgressTests
    {
        const string UxmlPath = "Assets/Hone/UI/Progress/Progress.uxml";
        const string PanelSettingsPath = "Assets/Hone/Sandbox/PanelSettings.asset";
        const string SandboxUxmlPath = "Assets/Hone/Sandbox/Sandbox.uxml";
        const int MaxWaitFrames = 60;
        const float TrackWidth = 200f;

        readonly List<Object> m_Created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in m_Created)
                Object.Destroy(obj);
            m_Created.Clear();
        }

        static VisualElement TrackOf(Hone.Progress progress) => progress.Q(className: "hone-progress__track");
        static VisualElement IndicatorOf(Hone.Progress progress) => progress.Q(className: "hone-progress__indicator");
        static VisualElement TitleContainerOf(Hone.Progress progress) => progress.Q(className: "hone-progress__title-container");

        // UXML の使用例が Hone.Progress になり、基底の unity-progress-bar 系のクラスが hone-progress 系に付け替わっている
        [Test]
        public void Uxml_HasProgressClasses_AndNoProgressBarClasses()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(tree, $"{UxmlPath} was not found");

            var progress = tree.Instantiate().Q<Hone.Progress>();
            Assert.IsNotNull(progress, "Progress.uxml must have a hone:Progress");
            Assert.IsTrue(progress.ClassListContains("hone-progress"));
            Assert.IsFalse(progress.ClassListContains(AbstractProgressBar.ussClassName));

            var pairs = new[]
            {
                (AbstractProgressBar.containerUssClassName, "hone-progress__container"),
                (AbstractProgressBar.backgroundUssClassName, "hone-progress__track"),
                (AbstractProgressBar.progressUssClassName, "hone-progress__indicator"),
                (AbstractProgressBar.titleContainerUssClassName, "hone-progress__title-container"),
                (AbstractProgressBar.titleUssClassName, "hone-progress__title"),
            };
            foreach (var (unityClass, honeClass) in pairs)
            {
                Assert.IsNull(progress.Q(className: unityClass), $"{unityClass} must be replaced");
                Assert.IsNotNull(progress.Q(className: honeClass), $"no .{honeClass}");
            }
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
            var go = new GameObject("ProgressTests");
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

        // Progress.uxml を Instantiate して、幅 TrackWidth の縦並びの枠に置き、track がレイアウトされるまで待つ
        IEnumerator PlaceProgress(System.Action<Hone.Progress> onReady)
        {
            VisualElement root = null;
            yield return CreatePanel(r => root = r);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(tree, $"{UxmlPath} was not found");
            var column = new VisualElement();
            column.style.width = TrackWidth;
            column.Add(tree.Instantiate());
            root.Add(column);
            var progress = column.Q<Hone.Progress>();
            Assert.IsNotNull(progress);
            var frames = 0;
            while (frames < MaxWaitFrames && !(TrackOf(progress).layout.width > 0f))
            {
                frames++;
                yield return null;
            }
            Assert.Greater(TrackOf(progress).layout.width, 0f, "the track was not laid out");
            onReady(progress);
        }

        // (B) value を変えると、進捗の要素（indicator）の幅が track の幅の value / highValue の割合になる。
        // 基底は value を設定した時点と track の大きさが変わった時点で、indicator の inline の right を px で書き換える。書き換えた right がレイアウトに出るまで待ってから測る。
        // 基底は right を「(track の幅 - 2) × (1 - 割合)」で決める（2 は既定テーマの track の左右の枠の分。Progress は枠を持たない）。
        // そのため 0 より大きい値では、幅は「2 + (track の幅 - 2) × 割合」になり、割合ちょうどの幅より最大 2px 広い。0 では基底が幅を 0 にする（6000.7.0b4 で測った）
        [UnityTest]
        public IEnumerator Value_SetsIndicatorWidthToRatioOfTrack()
        {
            Hone.Progress progress = null;
            yield return PlaceProgress(p => progress = p);
            var track = TrackOf(progress);
            var indicator = IndicatorOf(progress);
            Assert.AreEqual(TrackWidth, track.layout.width, 0.01f);

            foreach (var value in new[] { 0f, 25f, 60f, 100f })
            {
                progress.value = value;
                var ratio = value / progress.highValue;
                var expected = ratio > 0f ? 2f + (TrackWidth - 2f) * ratio : 0f;
                var frames = 0;
                // layout の幅は画素に丸められるので 0.5px まで許す
                while (frames < MaxWaitFrames && Mathf.Abs(indicator.layout.width - expected) > 0.5f)
                {
                    frames++;
                    yield return null;
                }
                Assert.AreEqual(expected, indicator.layout.width, 0.5f, $"value {value}");
                Assert.AreEqual(TrackWidth * ratio, indicator.layout.width, 2f, $"value {value}: the width must be within 2px of the ratio of the track");
                Assert.AreEqual(0f, indicator.layout.x, 0.01f, $"value {value}: the indicator must start at the left edge of the track");
                Assert.AreEqual(track.layout.height, indicator.layout.height, 0.01f, $"value {value}: the indicator must fill the height of the track");
            }
        }

        // (B) track の高さが --hone-size-progress（8px）、角丸が --hone-radius-progress（4px）、溝の色が --hone-color-progress-track、進捗の色が --hone-color-primary。
        // title は既定で表示しない（title を設定しても display: none）
        [UnityTest]
        public IEnumerator Style_ResolvesFromTokens_AndHidesTitle()
        {
            Hone.Progress progress = null;
            yield return PlaceProgress(p => progress = p);
            progress.title = "Downloading";
            yield return null;

            var track = TrackOf(progress).resolvedStyle;
            Assert.AreEqual(8f, track.height);
            Assert.AreEqual(4f, track.borderTopLeftRadius);
            Assert.AreEqual(4f, track.borderTopRightRadius);
            Assert.AreEqual(4f, track.borderBottomLeftRadius);
            Assert.AreEqual(4f, track.borderBottomRightRadius);
            Assert.AreEqual(new Color(24f / 255f, 24f / 255f, 27f / 255f, 0.2f), track.backgroundColor);
            Assert.AreEqual(8f, progress.resolvedStyle.height, "the root must be as tall as the track");

            var indicator = IndicatorOf(progress).resolvedStyle;
            Assert.AreEqual(new Color32(0x18, 0x18, 0x1b, 0xff), (Color32)indicator.backgroundColor);
            Assert.AreEqual(4f, indicator.borderTopLeftRadius);
            Assert.AreEqual(4f, indicator.borderTopRightRadius);
            Assert.AreEqual(4f, indicator.borderBottomLeftRadius);
            Assert.AreEqual(4f, indicator.borderBottomRightRadius);

            Assert.AreEqual(DisplayStyle.None, TitleContainerOf(progress).resolvedStyle.display);
        }
    }
}
#endif
