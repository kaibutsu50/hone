namespace Hone.Core
{
    // BackStack に積める「Cancel で閉じるもの」。Dialog、Popover など。
    public interface IDismissable
    {
        // 閉じる。閉じ終わったら、自分で BackStack.Remove を呼ぶこと（BackStack.HandleCancel は Remove しない）。
        void Dismiss();
    }
}
