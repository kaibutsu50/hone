using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Experiments
{
    // #15 の実験用。TokensExp.uxml の Label が解決した FontDefinition をログ（[TokensExpProbe]）に出す。判定はこのログを正とし、スクリーンショットは補助に使う。
    //   hone:   Sandbox/PanelSettings.asset の theme（HoneTheme.tss。--hone-font-body を :root に project:// で定義）。Sandbox/Fonts/RobotoMono.asset の参照元はこの :root の 1 箇所だけ
    //   nofont: TokensExpNoFont.tss（--hone-font-body を定義しない。:root に url("/Assets/…") と resource("…") の変数を置く）
    // Resources 側の複製（Fonts/RobotoMono）と Sandbox/Fonts 側は同名なので、Resources.Load で取った複製との同一性（kind=resources-copy）で区別する。
    // ring: .hone-focusable の Button に Focus() し、border の色と幅を focus 前後と対照（ring-control）で記録する。focus 後も既定テーマの :focus の色のままなら FAIL。
    // Player では -tokensexp-shot <path> を渡すとスクリーンショットを保存して終了する。Editor の Play Mode では Application.Quit が効かないので終了しない。
    // 測定の前提が崩れたとき（root 未取得、Label の欠落、control が既定フォントでない、hone.body が :root の変数のフォントでない、
    // nofont の path / res が Resources 側の複製でない、Error のログ、撮影失敗）は LogError にし、-tokensexp-shot 指定時は終了コード 1 で終了する。
    // 最後に必ず "RESULT OK|FAIL" の行を出す。この行が無いログは、途中で止まったものとして扱う。
    public class TokensExpProbe : MonoBehaviour
    {
        const int MaxWaitFrames = 300;
        const float MaxWaitSeconds = 30f;
        const int SettleFrames = 10;
        const int MaxMessages = 20;
        // 既定テーマの Button の :focus の枠の色（#13 の測定値）。Core.uss の ring がこれに負けたら FAIL にする
        static readonly Color32 DefaultThemeFocusColor = new Color32(0, 106, 166, 255);
        static readonly string[] LabelNames = { "control", "body", "plain-child", "inherit", "path", "res" };

        [SerializeField] PanelRenderer m_Hone;
        [SerializeField] PanelRenderer m_NoFont;

        VisualElement m_HoneRoot;
        VisualElement m_NoFontRoot;
        bool m_Failed;
        int m_ErrorCount;
        int m_WarningCount;
        readonly List<string> m_Messages = new List<string>();

        void Awake()
        {
            Application.logMessageReceived += OnLog;
            if (m_Hone == null || m_NoFont == null)
            {
                Fail($"PanelRenderer is not assigned (hone={m_Hone != null} nofont={m_NoFont != null})");
                return;
            }

            // PanelRenderer の root は public では reload callback 経由でしか取れない
            m_Hone.RegisterUIReloadCallback((pr, root, version) => m_HoneRoot = root);
            m_NoFont.RegisterUIReloadCallback((pr, root, version) =>
            {
                m_NoFontRoot = root;
                // 2 枚の PanelRenderer が重ならないよう、nofont を右半分に置く（見た目だけの都合）
                var stage = root.Q("stage");
                if (stage != null)
                    stage.style.marginLeft = Length.Percent(50);
            });
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        IEnumerator Start()
        {
            Debug.Log($"[TokensExpProbe] isEditor={Application.isEditor} unity={Application.unityVersion} platform={Application.platform}");

            var hasShot = TryGetArg("-tokensexp-shot", out var shot);
            if (hasShot && shot == null)
                Fail("-tokensexp-shot needs a path");

            // style の解決前は fontAsset が null になり、変数が解決できなかった場合と区別が付かない。
            // 解決されるはずの Label（hone.body、nofont の path と res）が揃うまで、上限付きで待つ。
            var resourcesCopy = Resources.Load<FontAsset>("Fonts/RobotoMono");
            if (resourcesCopy == null)
                Fail("Resources.Load<FontAsset>(\"Fonts/RobotoMono\") returned null");

            var frames = 0;
            var deadline = Time.realtimeSinceStartup + MaxWaitSeconds;
            while (!m_Failed && frames < MaxWaitFrames && Time.realtimeSinceStartup < deadline && !ExpectedResolved())
            {
                frames++;
                yield return null;
            }
            // null のままのはずの Label が後から動く場合に備えて、少し待ってから読む
            for (var i = 0; i < SettleFrames; i++)
                yield return null;

            if (!m_Failed)
            {
                if (m_HoneRoot == null || m_NoFontRoot == null)
                    Fail($"UI reload callback was not invoked within {frames} frames (hone={m_HoneRoot != null} nofont={m_NoFontRoot != null})");
                else
                    Measure(resourcesCopy, frames);
            }

            if (!m_Failed && m_HoneRoot != null)
            {
                // 2 枚目の panel（nofont）の要素は Focus() してもフォーカスを取れなかった（原因は未確認）。ring は hone の panel だけで測る
                var ring = m_HoneRoot.Q<Button>("ring");
                var control = m_HoneRoot.Q<Button>("ring-control");
                if (ring == null || control == null)
                {
                    Fail("button 'hone.ring' or 'hone.ring-control' not found");
                }
                else
                {
                    LogBorder("hone", "ring-control", control);
                    LogBorder("hone", "ring-before", ring);
                    ring.Focus();
                    // フォーカス変更は非同期。値を読む前に少し待つ
                    for (var i = 0; i < SettleFrames; i++)
                        yield return null;
                    LogBorder("hone", "ring-focused", ring);
                    if (ring.focusController?.focusedElement != ring)
                        Fail("premise broken: hone.ring did not get focus");
                    else if (IsSameColor(ring.resolvedStyle.borderTopColor, DefaultThemeFocusColor))
                        Fail("hone.ring kept the default theme's :focus border color (Core.uss .hone-focusable lost to it)");
                }
            }

            if (hasShot && shot != null)
            {
                // スプラッシュのフェード中に撮ると全体が暗く写る
                deadline = Time.realtimeSinceStartup + MaxWaitSeconds;
                while (!UnityEngine.Rendering.SplashScreen.isFinished && Time.realtimeSinceStartup < deadline)
                    yield return null;

                yield return new WaitForEndOfFrame();
                if (TryCapture(shot))
                {
                    deadline = Time.realtimeSinceStartup + MaxWaitSeconds;
                    while (!File.Exists(shot) && Time.realtimeSinceStartup < deadline)
                        yield return null;
                    for (var i = 0; i < 10; i++)
                        yield return null;
                    if (!File.Exists(shot))
                        Fail($"screenshot was not written: {shot}");
                }
                else
                {
                    m_Failed = true;
                }
            }

            Debug.Log($"[TokensExpProbe] messages error={m_ErrorCount} warning={m_WarningCount}");
            foreach (var message in m_Messages)
                Debug.Log($"[TokensExpProbe] message {message}");
            Debug.Log($"[TokensExpProbe] RESULT {(m_Failed ? "FAIL" : "OK")}");

            if (hasShot)
                Application.Quit(m_Failed ? 1 : 0);
        }

        IEnumerable<(string, VisualElement)> Panels()
        {
            yield return ("hone", m_HoneRoot);
            yield return ("nofont", m_NoFontRoot);
        }

        static bool IsSameColor(Color a, Color32 b)
        {
            var c = (Color32)a;
            return c.r == b.r && c.g == b.g && c.b == b.b && c.a == b.a;
        }

        static void LogBorder(string panel, string name, VisualElement element)
        {
            var style = element.resolvedStyle;
            var c = (Color32)style.borderTopColor;
            Debug.Log($"[TokensExpProbe] {panel}.{name}: borderTopColor=rgb({c.r},{c.g},{c.b}) borderTopWidth={style.borderTopWidth}");
        }

        bool ExpectedResolved()
        {
            if (m_HoneRoot == null || m_NoFontRoot == null)
                return false;
            return Resolved(m_HoneRoot, "body") && Resolved(m_NoFontRoot, "path") && Resolved(m_NoFontRoot, "res");
        }

        static bool Resolved(VisualElement root, string name)
        {
            var label = root.Q<Label>(name);
            return label != null && label.resolvedStyle.unityFontDefinition.fontAsset != null;
        }

        void Measure(FontAsset resourcesCopy, int frames)
        {
            var kinds = new Dictionary<string, string>();
            foreach (var (panel, root) in Panels())
            {
                foreach (var name in LabelNames)
                {
                    var label = root.Q<Label>(name);
                    if (label == null)
                    {
                        Fail($"label '{panel}.{name}' not found");
                        continue;
                    }

                    var def = label.resolvedStyle.unityFontDefinition;
                    string kind;
                    string asset;
                    if (def.fontAsset == null)
                    {
                        kind = "null";
                        asset = "null";
                    }
                    else
                    {
                        kind = ReferenceEquals(def.fontAsset, resourcesCopy) ? "resources-copy" : "other";
                        asset = $"{def.fontAsset.name}#{def.fontAsset.GetEntityId()}";
                    }
                    kinds[$"{panel}.{name}"] = kind;
                    var font = def.font != null ? def.font.name : "null";
                    Debug.Log($"[TokensExpProbe] {panel}.{name}: fontAsset={asset} kind={kind} font={font}");
                }
            }
            Debug.Log($"[TokensExpProbe] resourcesCopy={resourcesCopy.name}#{resourcesCopy.GetEntityId()} waitedFrames={frames}");

            // 測定の前提。ここが崩れたら、上の値は読めない
            Expect(kinds, "hone.control", "null");
            Expect(kinds, "nofont.control", "null");
            Expect(kinds, "hone.body", "other");
            Expect(kinds, "nofont.path", "resources-copy");
            Expect(kinds, "nofont.res", "resources-copy");
            Expect(kinds, "hone.plain-child", "resources-copy");
            Expect(kinds, "nofont.plain-child", "resources-copy");
        }

        void Expect(Dictionary<string, string> kinds, string key, string expected)
        {
            if (!kinds.TryGetValue(key, out var actual))
                return; // 欠落は上で Fail 済み
            if (actual != expected)
                Fail($"premise broken: {key} kind={actual} (expected {expected})");
        }

        void Fail(string message)
        {
            m_Failed = true;
            Debug.LogError($"[TokensExpProbe] FAIL {message}");
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            // Probe 自身の出力は集計しない（Fail を二重に数えない）
            if (condition.StartsWith("[TokensExpProbe]"))
                return;

            if (type == LogType.Warning)
                m_WarningCount++;
            else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                m_ErrorCount++;
                m_Failed = true;
            }
            else
                return;

            if (m_Messages.Count < MaxMessages)
                m_Messages.Add($"{type}: {condition}");
        }

        static bool TryCapture(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                ScreenCapture.CaptureScreenshot(path);
                return true;
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[TokensExpProbe] failed to capture screenshot to '{path}': {e.Message}");
                return false;
            }
        }

        // フラグが無ければ false。フラグがあって値が無い（末尾、または次が別のフラグ）ときは true で value = null
        static bool TryGetArg(string key, out string value)
        {
            value = null;
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] != key)
                    continue;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    value = args[i + 1];
                return true;
            }
            return false;
        }
    }
}
