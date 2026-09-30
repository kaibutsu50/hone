using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Experiments
{
    // --hone-font-body（theme の :root）と .hone-text、.hone-focusable の focus ring の検証（#15）。
    // TokensExp.uxml の Label が解決した FontDefinition と、ring の Button の border をログ（[TokensExpProbe]）に出す。判定はこのログを正とし、スクリーンショットは補助に使う。
    //   hone:   Sandbox/PanelSettings.asset の theme（HoneTheme.tss。--hone-font-body を :root に project:// で定義）。
    //           TokensExp.unity の依存の中では、Sandbox/Fonts/RobotoMono.asset を直接参照するのはこの :root だけ（FontVar の USS からも参照されるが、このシーンの依存には入らない）
    //   nofont: TokensExpNoFont.tss（--hone-font-body を定義しない。:root に url("/Assets/…") と resource("…") の変数を置く）
    // Label の kind: Resources 側の複製（Fonts/RobotoMono）と同一なら resources-copy、同名の別アセット（Sandbox/Fonts 側）なら sandbox-font、それ以外の非 null は other。
    // ring: .hone-focusable の Button に Focus() し、border の色と幅を focus 前後と対照（ring-control）で記録する。focus 後の色と幅が --hone-color-ring / --hone-ring-width の値でなければ FAIL。
    // Player では -tokensexp-shot <path> を渡すとスクリーンショットを保存して終了する。Editor の Play Mode では Application.Quit が効かないので終了しない。
    // 測定の前提が崩れたとき（Fail と Expect を呼んでいる箇所。Error のログを含む）は LogError にし、-tokensexp-shot 指定時は終了コード 1 で終了する。
    // 最後に必ず "RESULT OK|FAIL" の行を出す。この行が無いログは、途中で止まったものとして扱う。
    // Probe の Awake より前に出た Error は集計されない。Player のログファイルは Error 行も目で確かめる。
    public class TokensExpProbe : MonoBehaviour
    {
        const int MaxWaitFrames = 300;
        const float MaxWaitSeconds = 30f;
        const int SettleFrames = 10;
        const int MaxMessages = 20;
        // Tokens.uss の --hone-color-ring（#71717a）と --hone-ring-width。トークンの値を変えたらここも変える
        static readonly Color32 ExpectedRingColor = new Color32(113, 113, 122, 255);
        const float ExpectedRingWidth = 2f;
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
                else
                    Fail("element 'stage' not found in nofont");
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
            if (!m_Failed && !ExpectedResolved())
                Fail($"timed out waiting for hone.body / nofont.path / nofont.res to resolve ({frames} frames)");
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
                    var style = ring.resolvedStyle;
                    if (ring.focusController?.focusedElement != ring)
                        Fail("premise broken: hone.ring did not get focus");
                    else if (!IsSameColor(style.borderTopColor, ExpectedRingColor) || !Mathf.Approximately(style.borderTopWidth, ExpectedRingWidth))
                        Fail("hone.ring is not drawn with --hone-color-ring / --hone-ring-width (Core.uss .hone-focusable lost to another rule, or the tokens did not resolve)");
                }
            }

            if (hasShot && shot != null)
            {
                // スプラッシュのフェード中に撮ると全体が暗く写る
                deadline = Time.realtimeSinceStartup + MaxWaitSeconds;
                while (!UnityEngine.Rendering.SplashScreen.isFinished && Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (!UnityEngine.Rendering.SplashScreen.isFinished)
                    Fail($"splash screen did not finish within {MaxWaitSeconds} seconds");

                yield return new WaitForEndOfFrame();
                if (TryCapture(shot))
                {
                    // CaptureScreenshot は非同期に書く。前回のファイルは TryCapture が消してあるので、中身のあるファイルが現れるまで待つ
                    deadline = Time.realtimeSinceStartup + MaxWaitSeconds;
                    while (!HasContent(shot) && Time.realtimeSinceStartup < deadline)
                        yield return null;
                    for (var i = 0; i < 10; i++)
                        yield return null;
                    if (!HasContent(shot))
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

        static bool HasContent(string path)
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length > 0;
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
                        if (ReferenceEquals(def.fontAsset, resourcesCopy))
                            kind = "resources-copy";
                        else if (def.fontAsset.name == resourcesCopy.name)
                            kind = "sandbox-font";
                        else
                            kind = "other";
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
            Expect(kinds, "hone.body", "sandbox-font");
            Expect(kinds, "nofont.path", "resources-copy");
            Expect(kinds, "nofont.res", "resources-copy");
            Expect(kinds, "hone.plain-child", "resources-copy");
            Expect(kinds, "nofont.plain-child", "resources-copy");
            // README の結果の表に書いている値。変わったら README の表も直す
            Expect(kinds, "hone.inherit", "sandbox-font");
            Expect(kinds, "nofont.body", "null");
            Expect(kinds, "nofont.inherit", "resources-copy");
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
                // 前回のファイルが残っていると、今回の撮影が失敗しても存在確認が通ってしまう
                if (File.Exists(path))
                    File.Delete(path);
                ScreenCapture.CaptureScreenshot(path);
                return true;
            }
            catch (Exception e)
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
