using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Experiments
{
    // #11 の実験用。FontVar シーンの Label が解決した FontDefinition をログに出す。
    // 判定はこのログ（resolvedStyle）を正とし、スクリーンショットは補助に使う。
    // Player では -fontvar-shot <path> を渡すとスクリーンショットを保存して終了する。
    // 失敗（root 未取得、Label の欠落、撮影失敗）は LogError にし、-fontvar-shot 指定時は終了コード 1 で終了する。
    [RequireComponent(typeof(PanelRenderer))]
    public class FontVarProbe : MonoBehaviour
    {
        const int MaxWaitFrames = 300;
        const float MaxWaitSeconds = 30f;
        static readonly string[] ExpectedLabels = { "control", "direct", "case1", "case2", "case3", "case4" };

        VisualElement m_Root;

        void Awake()
        {
            // PanelRenderer の root は public では reload callback 経由でしか取れない
            GetComponent<PanelRenderer>().RegisterUIReloadCallback((panelRenderer, root, version) => m_Root = root);
        }

        IEnumerator Start()
        {
            Debug.Log($"[FontVarProbe] isEditor={Application.isEditor} unity={Application.unityVersion} platform={Application.platform}");

            var hasShot = TryGetArg("-fontvar-shot", out var shot);
            var failed = false;
            if (hasShot && shot == null)
            {
                Debug.LogError("[FontVarProbe] -fontvar-shot needs a path");
                Application.Quit(1);
                yield break;
            }

            // style の解決前は fontAsset が null になり、変数が解決できなかった場合と区別が付かない。
            // control 以外の全 Label が解決されるまで上限付きで待つ。
            var frames = 0;
            while (frames < MaxWaitFrames && !AllResolved())
            {
                frames++;
                yield return null;
            }

            if (m_Root == null)
            {
                Debug.LogError($"[FontVarProbe] UI reload callback was not invoked within {frames} frames");
                failed = true;
            }
            else
            {
                var found = 0;
                foreach (var name in ExpectedLabels)
                {
                    var label = m_Root.Q<Label>(name);
                    if (label == null)
                    {
                        Debug.LogError($"[FontVarProbe] label '{name}' not found");
                        failed = true;
                        continue;
                    }

                    found++;
                    var def = label.resolvedStyle.unityFontDefinition;
                    // 同名の FontAsset が Fonts/ と Resources/ の 2 つあるので entity ID も出す
                    var asset = def.fontAsset != null ? $"{def.fontAsset.name}#{def.fontAsset.GetEntityId()}" : "null";
                    var font = def.font != null ? def.font.name : "null";
                    var suffix = name != "control" && def.fontAsset == null ? $" (still null after {frames} frames)" : "";
                    Debug.Log($"[FontVarProbe] {name}: fontAsset={asset} font={font}{suffix}");
                }
                Debug.Log($"[FontVarProbe] labels={found}/{ExpectedLabels.Length} waitedFrames={frames}");
            }

            if (!hasShot)
                yield break;

            // スプラッシュのフェード中に撮ると全体が暗く写る
            var deadline = Time.realtimeSinceStartup + MaxWaitSeconds;
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
                {
                    Debug.LogError($"[FontVarProbe] screenshot was not written: {shot}");
                    failed = true;
                }
            }
            else
            {
                failed = true;
            }

            Application.Quit(failed ? 1 : 0);
        }

        bool AllResolved()
        {
            if (m_Root == null)
                return false;
            foreach (var name in ExpectedLabels)
            {
                var label = m_Root.Q<Label>(name);
                if (label == null)
                    return false;
                if (name != "control" && label.resolvedStyle.unityFontDefinition.fontAsset == null)
                    return false;
            }
            return true;
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
                Debug.LogError($"[FontVarProbe] failed to capture screenshot to '{path}': {e.Message}");
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
