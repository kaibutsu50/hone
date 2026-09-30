using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Hone.Core
{
    // panel ごとの「開いているものの積み重ね」。NavigationCancelEvent（ゲームパッドの B、Esc）が来たら最前面だけを閉じる。
    // NavigationCancelEvent は UI Toolkit では bubble するだけで既定の動作が無いので、ここで意味付けする。
    public sealed class BackStack
    {
        static readonly ConditionalWeakTable<IPanel, BackStack> s_Instances = new ConditionalWeakTable<IPanel, BackStack>();

        readonly IPanel m_Panel;
        readonly VisualElement m_Root;
        readonly List<IDismissable> m_Items = new List<IDismissable>();

        BackStack(IPanel panel)
        {
            m_Panel = panel;
            m_Root = panel.visualTree;
            // TextField など一部の組み込み要素は Cancel を自分で処理して propagation を止めるので、bubble up では届かないことがある。
            // root の trickle down 段階なら必ず先に見える。
            m_Root.RegisterCallback<NavigationCancelEvent>(OnNavigationCancel, TrickleDown.TrickleDown);
            m_Root.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        public static BackStack For(IPanel panel)
        {
            if (panel == null)
                throw new ArgumentNullException(nameof(panel));
            return s_Instances.GetValue(panel, p => new BackStack(p));
        }

        // 同じ item を二度積むと最前面へ移る（同じ item が重複して積まれないようにする）
        public void Push(IDismissable item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            m_Items.Remove(item);
            m_Items.Add(item);
        }

        // 途中の要素も外せる。Dialog が Cancel 以外の手段で閉じたときに呼ぶ。積まれていなければ何もしない
        public void Remove(IDismissable item)
        {
            m_Items.Remove(item);
        }

        // 最前面の Dismiss() を呼ぶ。空なら false。Remove はしない（Dismiss が遅延する場合に備え、閉じ終わった側が外す）
        public bool HandleCancel()
        {
            if (m_Items.Count == 0)
                return false;
            m_Items[m_Items.Count - 1].Dismiss();
            return true;
        }

        void OnNavigationCancel(NavigationCancelEvent evt)
        {
            // 空なら止めずに流す。利用者側のハンドラに届く
            if (m_Items.Count == 0)
                return;
            // 編集中の TextField の Cancel は編集のキャンセルであって、閉じる操作ではない
            if (IsEditingTextField(evt.target as VisualElement))
                return;
            // 先に止める。Dismiss が例外を投げても、Cancel を利用者側のハンドラへ流さない
            evt.StopPropagation();
            HandleCancel();
        }

        static bool IsEditingTextField(VisualElement target)
        {
            var field = target as TextField ?? target?.GetFirstAncestorOfType<TextField>();
            return field != null && !field.isReadOnly;
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            m_Root.UnregisterCallback<NavigationCancelEvent>(OnNavigationCancel, TrickleDown.TrickleDown);
            m_Root.UnregisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            m_Items.Clear();
            s_Instances.Remove(m_Panel);
        }
    }
}
