using System;
using System.Runtime.ExceptionServices;

namespace ActorCoreFramework
{
    /// <summary>
    /// 後始末や一括配送の途中で出た例外を集める。
    ///
    /// 1件の失敗で残りの処理を打ち切らないために、例外は捕捉して先へ進む。
    /// 最初の1件はスタックトレースを保ったまま呼び出し元へ投げ直し、
    /// 2件目以降はその場でログへ出す。控えるのを最初の1件だけにすると、
    /// 同じフレームで起きた後続の例外が誰の目にも触れずに消える。
    ///
    /// 構造体なのでコピーすると集めた結果が分かれる。ローカル変数に置き、
    /// 他のメソッドへ渡すときはrefで渡すこと。
    /// </summary>
    internal struct FailureCollector
    {
        ExceptionDispatchInfo? first;

        public void Add(Exception exception)
        {
            if (first == null)
            {
                first = ExceptionDispatchInfo.Capture(exception);
                return;
            }

            UnityEngine.Debug.LogException(exception);
        }

        public void ThrowIfAny() => first?.Throw();
    }
}
