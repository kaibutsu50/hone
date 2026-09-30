using System;
using System.Collections;
using System.Collections.Generic;
using Hone.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hone.Sandbox.Tests
{
    // Editor で実行する前提（PanelSettings と UXML を AssetDatabase から読む）
    public class BackStackTests
    {
        const string PanelSettingsPath = "Assets/Hone/Sandbox/PanelSettings.asset";
        const string UxmlPath = "Assets/Hone/Sandbox/Sandbox.uxml";
        const int MaxWaitFrames = 60;

        class Dismissable : IDismissable
        {
            public int Calls;
            public void Dismiss() => Calls++;
        }

        readonly List<Object> m_Created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in m_Created)
                Object.Destroy(obj);
            m_Created.Clear();
        }

        // PanelRenderer を 1 つ作り、UI が読み込まれたら root を返す。panelSettings ごとに別の panel になる
        IEnumerator CreatePanel(PanelSettings panelSettings, Action<VisualElement> onReady)
        {
            var go = new GameObject("BackStackTests");
            m_Created.Add(go);
            // 有効化の前に callback を登録する（読み込み済みの後に登録して取りこぼすのを避ける）
            go.SetActive(false);
            var panelRenderer = go.AddComponent<PanelRenderer>();
            panelRenderer.panelSettings = panelSettings;
            panelRenderer.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
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

        PanelSettings LoadPanelSettings()
        {
            return AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        }

        // 同じ asset を共有すると同じ panel になるので、別 panel が要るときは複製を使う
        PanelSettings ClonePanelSettings()
        {
            var clone = Object.Instantiate(LoadPanelSettings());
            m_Created.Add(clone);
            return clone;
        }

        static void SendCancel(VisualElement target)
        {
            using (var evt = NavigationCancelEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        [UnityTest]
        public IEnumerator Cancel_DismissesOnlyTopmostAndStopsEvent()
        {
            VisualElement root = null;
            yield return CreatePanel(LoadPanelSettings(), r => root = r);
            var below = new Dismissable();
            var top = new Dismissable();
            var stack = BackStack.For(root.panel);
            stack.Push(below);
            stack.Push(top);
            var child = new VisualElement();
            root.Add(child);
            var reachedTarget = 0;
            child.RegisterCallback<NavigationCancelEvent>(evt => reachedTarget++);

            SendCancel(child);

            Assert.AreEqual(1, top.Calls);
            Assert.AreEqual(0, below.Calls);
            Assert.AreEqual(0, reachedTarget, "the event must be stopped before reaching the target");
        }

        [UnityTest]
        public IEnumerator Cancel_EmptyStack_DoesNotStopEvent()
        {
            VisualElement root = null;
            yield return CreatePanel(LoadPanelSettings(), r => root = r);
            BackStack.For(root.panel);
            var child = new VisualElement();
            root.Add(child);
            var reachedRoot = 0;
            root.RegisterCallback<NavigationCancelEvent>(evt => reachedRoot++);

            SendCancel(child);

            Assert.AreEqual(1, reachedRoot);
        }

        [UnityTest]
        public IEnumerator Remove_MiddleItem_CancelDismissesRemainingTopmost()
        {
            VisualElement root = null;
            yield return CreatePanel(LoadPanelSettings(), r => root = r);
            var bottom = new Dismissable();
            var middle = new Dismissable();
            var top = new Dismissable();
            var stack = BackStack.For(root.panel);
            stack.Push(bottom);
            stack.Push(middle);
            stack.Push(top);

            stack.Remove(middle);
            Assert.IsTrue(stack.HandleCancel());
            stack.Remove(top);
            Assert.IsTrue(stack.HandleCancel());

            Assert.AreEqual(1, top.Calls);
            Assert.AreEqual(0, middle.Calls);
            Assert.AreEqual(1, bottom.Calls);
        }

        [UnityTest]
        public IEnumerator Cancel_EditingTextField_DoesNotDismiss()
        {
            VisualElement root = null;
            yield return CreatePanel(LoadPanelSettings(), r => root = r);
            var item = new Dismissable();
            BackStack.For(root.panel).Push(item);
            var textField = new TextField();
            root.Add(textField);
            textField.Focus();
            // フォーカス変更は非同期
            yield return null;
            var reachedTarget = 0;
            textField.RegisterCallback<NavigationCancelEvent>(evt => reachedTarget++);

            SendCancel(textField);

            Assert.AreEqual(0, item.Calls);
            Assert.AreEqual(1, reachedTarget, "the event must not be stopped");
        }

        [UnityTest]
        public IEnumerator For_SamePanelReturnsSameInstance_OtherPanelReturnsOther()
        {
            VisualElement rootA = null;
            VisualElement rootB = null;
            yield return CreatePanel(LoadPanelSettings(), r => rootA = r);
            yield return CreatePanel(ClonePanelSettings(), r => rootB = r);
            Assume.That(rootA.panel, Is.Not.SameAs(rootB.panel), "the two PanelRenderers must not share a panel");

            var a = BackStack.For(rootA.panel);

            Assert.AreSame(a, BackStack.For(rootA.panel));
            Assert.AreNotSame(a, BackStack.For(rootB.panel));
        }
    }
}
