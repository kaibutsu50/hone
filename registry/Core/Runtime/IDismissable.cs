namespace Hone.Core
{
    // BackStack に積める「Cancel で閉じるもの」。Dialog、Popover など。
    public interface IDismissable
    {
        // 閉じる。閉じ終わったら、自分で BackStack.Remove を呼ぶこと（BackStack.HandleCancel は Remove しない）。
        // Remove されるまでは Cancel のたびに繰り返し呼ばれうるので、冪等に実装すること。
        // 例外を投げても項目はスタックに残る。また、要素が panel から外れるときにも Remove すること（残ると以後の Cancel が詰まる）。
        void Dismiss();
    }
}
