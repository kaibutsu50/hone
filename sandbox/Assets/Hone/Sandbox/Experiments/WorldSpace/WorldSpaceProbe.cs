using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Experiments
{
    // World Space の panel に、カメラ経由の入力（PanelInputConfiguration + EventSystem + InputSystemUIInputModule）が届くかの検証。
    // Input System の合成 Mouse を作り、Button の位置を screen 座標にしてポインタを動かし、press / release を積む。clicked が 1 回発火したら RESULT 行を出す。
    //
    // Button を panel の中央に置いてあるので、その中心の world 座標を PanelRenderer の transform.position とみなして Camera.WorldToScreenPoint で screen 座標にする
    // （要素の panel 座標から world への一般の変換は書かない）。Screen.width などは見ない。
    // 判定するのは測定の前提（panel の準備、Button の worldBound と型、シーンの構成、Error / Exception のログ、時間切れ）と、clicked が 1 回届いたこと。
    // 失敗はすべて [WorldSpaceProbe] FAIL 行で出し、最後に FAIL total=… を出して終了コード 1 で終了する。全部通れば DONE failed=0 を出して終了コード 0 で終了する。
    // 前提が崩れたときはクリックせず、RESULT 行を出さない。
    // Editor の Play Mode では Application.Quit が効かないので、Player で回す。
    [RequireComponent(typeof(PanelRenderer))]
    public class WorldSpaceProbe : MonoBehaviour
    {
        const int MaxWaitFrames = 300;
        const int SettleFrames = 5;
        const float MaxRunSeconds = 60f;
        const string Prefix = "[WorldSpaceProbe]";

        VisualElement m_Root;
        bool m_Finished;
        readonly List<string> m_Failures = new List<string>();

        void Awake()
        {
            // panel の準備中に出た Error / Exception も拾うため、最初に購読する
            Application.logMessageReceived += OnLog;
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
                StopAllCoroutines();
                Fail($"did not finish within {MaxRunSeconds}s");
                Finish();
            }
        }

        IEnumerator Start()
        {
            // ウィンドウが非アクティブな Player でも回し続ける。Input System は既定ではフォーカスの無いウィンドウへの入力（合成デバイス分も含む）を捨てる
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

            var frames = 0;
            var ready = IsReady();
            while (frames < MaxWaitFrames && !ready)
            {
                frames++;
                yield return null;
                ready = IsReady();
            }

            if (!ready)
            {
                Fail($"panel was not ready within {frames} frames");
                Finish();
                yield break;
            }

            var button = m_Root.Q<UnityEngine.UIElements.Button>("target");
            var camera = Camera.main;
            CheckPrerequisites(frames, button, camera);
            if (m_Failures.Count > 0)
            {
                Finish();
                yield break;
            }

            var screen = camera.WorldToScreenPoint(transform.position);
            Debug.Log($"{Prefix} POINTER screen=({screen.x:F1},{screen.y:F1}) depth={screen.z:F2}");
            if (screen.z <= 0)
            {
                Fail($"the panel is behind the camera (depth={screen.z})");
                Finish();
                yield break;
            }

            var clicked = 0;
            button.clicked += () => clicked++;
            yield return Click(screen);

            // Click は release のあと SettleFrames 待ってから戻るので、ここで読む。1 回の press / release に対して 1 回だけ発火すること
            Debug.Log($"{Prefix} RESULT clicked={(clicked == 1 ? "true" : "false")} count={clicked}");
            if (clicked == 0)
                Fail("clicked did not fire");
            else if (clicked > 1)
                Fail($"clicked fired {clicked} times for one click");

            Finish();
        }

        bool IsReady()
        {
            if (m_Root == null || m_Root.panel == null)
                return false;
            var button = m_Root.Q<UnityEngine.UIElements.Button>("target");
            return button != null && button.worldBound.width > 0 && button.worldBound.height > 0;
        }

        void CheckPrerequisites(int waitedFrames, UnityEngine.UIElements.Button button, Camera camera)
        {
            var panelRenderer = GetComponent<PanelRenderer>();
            var panelSettings = panelRenderer.panelSettings;
            var eventSystem = FindAnyObjectByType<EventSystem>();
            var module = FindAnyObjectByType<InputSystemUIInputModule>();
            var config = FindAnyObjectByType<PanelInputConfiguration>();
            var mainIsEventCamera = config != null && camera != null &&
                (config.defaultEventCameraIsMainCamera || (config.eventCameras != null && config.eventCameras.Contains(camera)));
            Debug.Log(
                $"{Prefix} HEADER unity={Application.unityVersion} platform={Application.platform} isEditor={Application.isEditor} " +
                $"renderMode={(panelSettings != null ? panelSettings.renderMode.ToString() : "n/a")} sizeMode={panelRenderer.worldSpaceSizeMode} " +
                $"eventSystem={(eventSystem != null)} inputModule={(module != null)} panelInputConfiguration={(config != null)} " +
                $"processWorldSpaceInput={(config != null && config.processWorldSpaceInput)} camera={(camera != null ? camera.name : "null")} " +
                $"mainIsEventCamera={mainIsEventCamera} waitedFrames={waitedFrames}");
            Debug.Log($"{Prefix} HEADER button={button.GetType().FullName} worldBound={button.worldBound} panelPosition={transform.position}");

            // Probe は Assembly-CSharp にあり Hone.Button を参照できないので、型名で確かめる
            if (button.GetType().FullName != "Hone.Button")
                Fail($"the target is {button.GetType().FullName}, not Hone.Button");
            if (panelSettings == null || panelSettings.renderMode != PanelRenderMode.WorldSpace)
                Fail("the PanelSettings is not World Space");
            if (panelRenderer.worldSpaceSizeMode != WorldSpaceSizeMode.Fixed)
                Fail($"worldSpaceSizeMode is {panelRenderer.worldSpaceSizeMode}, not Fixed");
            if (eventSystem == null || module == null)
                Fail("no EventSystem with InputSystemUIInputModule in the scene");
            if (config == null || !config.processWorldSpaceInput)
                Fail("no PanelInputConfiguration with processWorldSpaceInput = true in the scene");
            if (camera == null)
                Fail("no camera tagged MainCamera");
            else if (config != null && !mainIsEventCamera)
                Fail("the Main Camera is not an event camera of the PanelInputConfiguration");
        }

        // 合成 Mouse を作り、ポインタを動かして hover を確定させてから、press → release を積む
        IEnumerator Click(Vector3 screen)
        {
            var mouse = InputSystem.AddDevice<Mouse>("WorldSpaceProbeMouse");
            try
            {
                var position = new Vector2(screen.x, screen.y);

                InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
                yield return Frames(SettleFrames);

                InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
                yield return Frames(SettleFrames);

                InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
                yield return Frames(SettleFrames);
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
            }
        }

        static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++)
                yield return null;
        }

        // 1 frame 待って、その間に出たログも数えてから終了する。終了コードは待った後の失敗の件数で決める
        void Finish()
        {
            if (m_Finished)
                return;
            m_Finished = true;
            StartCoroutine(QuitAfterFrame());
        }

        IEnumerator QuitAfterFrame()
        {
            yield return null;
            if (m_Failures.Count == 0)
            {
                Debug.Log($"{Prefix} DONE failed=0");
                Application.Quit(0);
            }
            else
            {
                Debug.LogError($"{Prefix} FAIL total={m_Failures.Count} first={m_Failures[0]}");
                Application.Quit(1);
            }
        }

        void Fail(string reason)
        {
            m_Failures.Add(reason);
            Debug.LogError($"{Prefix} FAIL {reason}");
        }

        // Probe 自身のログ（Fail の LogError を含む）は数えない。それ以外の Error / Exception / Assert は FAIL にする
        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log || type == LogType.Warning || condition.StartsWith(Prefix, StringComparison.Ordinal))
                return;
            Fail($"{type}: {condition}");
        }
    }
}
