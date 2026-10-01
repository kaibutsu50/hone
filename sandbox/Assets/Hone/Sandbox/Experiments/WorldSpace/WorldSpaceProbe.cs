using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Experiments
{
    // World Space の panel に、カメラ経由の入力（PanelInputConfiguration + EventSystem + InputSystemUIInputModule）が届くかの検証。
    // Input System の合成 Mouse を作り、Button の位置を screen 座標にしてポインタを動かし、press / release を積む。clicked が発火したら RESULT 行を出す。
    //
    // Button を panel の中央に置いてあるので、その中心の world 座標を PanelRenderer の transform.position とみなして Camera.WorldToScreenPoint で screen 座標にする
    // （要素の panel 座標から world への一般の変換は書かない）。Screen.width などは見ない。
    // 判定するのは測定の前提（panel の準備、Button の worldBound、シーンの構成、Error / Exception のログ）と、clicked が届いたこと。崩れていれば FAIL を出し、終了コード 1 で終了する。
    // Editor の Play Mode では Application.Quit が効かないので、Player で回す。
    [RequireComponent(typeof(PanelRenderer))]
    public class WorldSpaceProbe : MonoBehaviour
    {
        const int MaxWaitFrames = 300;
        const int SettleFrames = 5;
        const float MaxRunSeconds = 60f;

        VisualElement m_Root;
        bool m_Finished;
        readonly List<string> m_Failures = new List<string>();

        void Awake()
        {
            // PanelRenderer の root は public では reload callback 経由でしか取れない
            GetComponent<PanelRenderer>().RegisterUIReloadCallback((panelRenderer, root, version) => m_Root = root);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        void Update()
        {
            if (!m_Finished && Time.realtimeSinceStartup > MaxRunSeconds)
            {
                m_Finished = true;
                Debug.LogError($"[WorldSpaceProbe] did not finish within {MaxRunSeconds}s. first failure: {(m_Failures.Count > 0 ? m_Failures[0] : "none")}");
                Application.Quit(1);
            }
        }

        IEnumerator Start()
        {
            // ウィンドウが非アクティブな Player でも回し続ける。Input System は既定ではフォーカスの無いウィンドウへの入力（合成デバイス分も含む）を捨てる
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

            var frames = 0;
            while (frames < MaxWaitFrames && !IsReady())
            {
                frames++;
                yield return null;
            }

            if (!IsReady())
            {
                m_Finished = true;
                Debug.LogError($"[WorldSpaceProbe] panel was not ready within {frames} frames");
                Application.Quit(1);
                yield break;
            }

            Application.logMessageReceived += OnLog;
            var button = m_Root.Q<UnityEngine.UIElements.Button>("target");
            var camera = Camera.main;
            var clicked = 0;
            button.clicked += () => clicked++;
            LogHeader(frames, button, camera);

            if (camera != null)
            {
                var screen = camera.WorldToScreenPoint(transform.position);
                Debug.Log($"[WorldSpaceProbe] POINTER screen=({screen.x:F1},{screen.y:F1}) depth={screen.z:F2}");
                if (screen.z <= 0)
                    Fail($"the panel is behind the camera (depth={screen.z})");
                else
                    yield return Click(screen);
            }

            // clicked は press → release の後、イベント配送を済ませた frame で数える
            Debug.Log($"[WorldSpaceProbe] RESULT clicked={clicked > 0}");
            if (clicked == 0)
                Fail("clicked did not fire");

            var failed = m_Failures.Count > 0;
            Debug.Log($"[WorldSpaceProbe] DONE failed={m_Failures.Count}{(failed ? $" first={m_Failures[0]}" : "")}");
            m_Finished = true;
            yield return null;
            Application.Quit(failed ? 1 : 0);
        }

        bool IsReady()
        {
            if (m_Root == null || m_Root.panel == null)
                return false;
            var button = m_Root.Q<UnityEngine.UIElements.Button>("target");
            return button != null && button.worldBound.width > 0 && button.worldBound.height > 0;
        }

        void LogHeader(int waitedFrames, UnityEngine.UIElements.Button button, Camera camera)
        {
            var eventSystem = FindAnyObjectByType<EventSystem>();
            var module = FindAnyObjectByType<InputSystemUIInputModule>();
            var config = FindAnyObjectByType<PanelInputConfiguration>();
            var panelSettings = GetComponent<PanelRenderer>().panelSettings;
            Debug.Log(
                $"[WorldSpaceProbe] HEADER unity={Application.unityVersion} platform={Application.platform} isEditor={Application.isEditor} " +
                $"renderMode={(panelSettings != null ? panelSettings.renderMode.ToString() : "n/a")} sizeMode={GetComponent<PanelRenderer>().worldSpaceSizeMode} " +
                $"eventSystem={(eventSystem != null)} inputModule={(module != null)} panelInputConfiguration={(config != null)} " +
                $"processWorldSpaceInput={(config != null && config.processWorldSpaceInput)} camera={(camera != null ? camera.name : "null")} waitedFrames={waitedFrames}");
            Debug.Log($"[WorldSpaceProbe] HEADER button={button.GetType().FullName} worldBound={button.worldBound} panelPosition={transform.position}");

            if (panelSettings == null || panelSettings.renderMode != PanelRenderMode.WorldSpace)
                Fail("the PanelSettings is not World Space");
            if (eventSystem == null || module == null)
                Fail("no EventSystem with InputSystemUIInputModule in the scene");
            if (config == null || !config.processWorldSpaceInput)
                Fail("no PanelInputConfiguration with processWorldSpaceInput = true in the scene");
            if (camera == null)
                Fail("no camera tagged MainCamera");
        }

        // 合成 Mouse を作り、ポインタを動かして hover を確定させてから、press → release を積む
        IEnumerator Click(Vector3 screen)
        {
            var mouse = InputSystem.AddDevice<Mouse>("WorldSpaceProbeMouse");
            var position = new Vector2(screen.x, screen.y);

            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            yield return Frames(SettleFrames);

            InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
            yield return Frames(SettleFrames);

            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            yield return Frames(SettleFrames);
        }

        static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++)
                yield return null;
        }

        void Fail(string reason)
        {
            m_Failures.Add(reason);
            Debug.LogError($"[WorldSpaceProbe] FAIL {reason}");
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log || type == LogType.Warning || condition.StartsWith("[WorldSpaceProbe]"))
                return;
            m_Failures.Add($"{type}: {condition}");
        }
    }
}
