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
    public class FontVarProbe : MonoBehaviour
    {
        const int WaitFrames = 5;

        VisualElement m_Root;

        void Awake()
        {
            // PanelRenderer の root は public では reload callback 経由でしか取れない
            GetComponent<PanelRenderer>().RegisterUIReloadCallback((panelRenderer, root, version) => m_Root = root);
        }

        IEnumerator Start()
        {
            for (var i = 0; i < WaitFrames; i++)
                yield return null;

            if (m_Root == null)
            {
                Debug.LogWarning("[FontVarProbe] UI reload callback was not invoked");
                yield break;
            }

            var root = m_Root;
            var count = 0;
            foreach (var label in root.Query<Label>().ToList())
            {
                var def = label.resolvedStyle.unityFontDefinition;
                var asset = def.fontAsset != null ? def.fontAsset.name : "null";
                var font = def.font != null ? def.font.name : "null";
                Debug.Log($"[FontVarProbe] {label.name}: fontAsset={asset} font={font}");
                count++;
            }
            Debug.Log($"[FontVarProbe] labels={count}");

            var shot = GetArg("-fontvar-shot");
            if (shot == null)
                yield break;

            // スプラッシュのフェード中に撮ると全体が暗く写る
            while (!UnityEngine.Rendering.SplashScreen.isFinished)
                yield return null;

            yield return new WaitForEndOfFrame();
            Directory.CreateDirectory(Path.GetDirectoryName(shot));
            ScreenCapture.CaptureScreenshot(shot);
            for (var i = 0; i < 10; i++)
                yield return null;
            Application.Quit();
        }

        static string GetArg(string key)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == key)
                    return args[i + 1];
            return null;
        }
    }
}
