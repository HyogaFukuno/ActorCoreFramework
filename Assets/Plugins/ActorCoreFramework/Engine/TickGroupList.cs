using System;
using System.Collections.Generic;

namespace ActorCoreFramework
{
    internal sealed class TickGroupList
    {
        readonly List<Actor> actors = new();

        /// <summary>
        /// Tick中に追加されたActor。反復中のリストを崩さないよう、Tickの完了後に挿入する。
        ///
        /// 削除は保留しない。登録解除はTickFunction.registeredを落とすことで表し、
        /// 反復ではそれを見て飛ばし、リストからは後でまとめて取り除く(Compact)。
        /// そのため同一フレームでAdd→Removeされても、挿入時にregisteredを見れば取りこぼさない。
        /// </summary>
        readonly List<Actor> pendingAdd = new();

        bool ticking;

        /// <summary>登録解除されたActorがリストに残っている間true。</summary>
        bool hasRemoved;

        public void Add(Actor actor)
        {
            if (ticking)
            {
                pendingAdd.Add(actor);
                return;
            }

            Insert(actor);
        }

        /// <summary>
        /// 登録解除を受け付ける。呼び出し側は先にTickFunction.registeredを落としておくこと。
        ///
        /// リストからの除去はCompactまで遅らせる。1体ずつList.Removeすると探索と詰め直しで
        /// O(n)かかり、大量に破棄したフレームがO(n^2)になるため。
        /// </summary>
        public void Remove(Actor actor) => hasRemoved = true;

        /// <summary>
        /// 登録解除されたActorをまとめて取り除く。並びは保たれる。
        /// Tick中は反復中のリストを崩すため何もしない(次の機会に持ち越す)。
        /// </summary>
        public void Compact()
        {
            if (!hasRemoved || ticking) { return; }

            hasRemoved = false;

            var count = 0;
            for (var i = 0; i < actors.Count; i++)
            {
                var actor = actors[i];
                if (!actor.PrimaryActorTick.registered) { continue; }

                actors[count++] = actor;
            }

            actors.RemoveRange(count, actors.Count - count);
        }

        /// <summary>
        /// Priority昇順の位置へ挿入する。
        /// 同値の場合は既存の要素の後ろに入るため、登録順が保たれる。
        /// 並びは常に整列済みなので、挿入位置は二分探索で求める。
        /// </summary>
        void Insert(Actor actor)
        {
            var priority = actor.PrimaryActorTick.Priority;

            // Priorityがpriorityを超える最初の位置(upper bound)
            var low = 0;
            var high = actors.Count;
            while (low < high)
            {
                var mid = (low + high) >> 1;
                if (actors[mid].PrimaryActorTick.Priority <= priority) { low = mid + 1; }
                else { high = mid; }
            }

            actors.Insert(low, actor);
        }

        /// <summary>
        /// グループ内のActorをPriority順にTickする。
        ///
        /// 1体が例外を投げても残りのActorはTickする。ここで打ち切ると、
        /// Priorityの高い1体の不具合で後続が丸ごとTickされなくなる。
        /// ただし例外を握り潰すと気付けないため、最初の例外はグループを回し終えてから
        /// 呼び出し元へ投げ直し(実行時はWorldLoopがログに出す)、2件目以降はその場でログに出す。
        /// </summary>
        public void Tick(float deltaTime)
        {
            Compact();

            ticking = true;

            var failures = new FailureCollector();

            try
            {
                // 途中でCountが変わらない前提。Tick中の追加分はpendingAddへ回る
                for (var i = 0; i < actors.Count; i++)
                {
                    var actor = actors[i];
                    var tick = actor.PrimaryActorTick;

                    // 登録解除済み、破棄予約済みのActorには、リストからの除去を待たずにTickを止める
                    if (!tick.registered || !actor.IsPlaying || actor.IsPendingDestroy)
                    {
                        continue;
                    }

                    if (!tick.Enabled)
                    {
                        // 停止中の時間を貯めない。貯めると再開した直後に、
                        // Intervalを待たずに発火してしまう。
                        tick.accumulator = 0.0f;
                        continue;
                    }

                    if (tick.Interval <= 0.0f)
                    {
                        Dispatch(actor, deltaTime, ref failures);
                        continue;
                    }

                    tick.accumulator += deltaTime;
                    if (tick.accumulator < tick.Interval)
                    {
                        continue;
                    }

                    // 超過分を次回へ持ち越し、間隔が実フレームレートに引きずられないようにする
                    // ただしスパイク後に何フレームも連続発火しないよう、持ち越しはInterval未満に丸める
                    var elapsed = tick.accumulator;
                    tick.accumulator %= tick.Interval;
                    elapsed -= tick.accumulator;

                    Dispatch(actor, elapsed, ref failures); // 間引いた分のDeltaをまとめて渡す
                }
            }
            finally
            {
                // Tick中に例外が出ても、状態と保留分は必ず反映してから抜ける
                ticking = false;
                FlushPending();
            }

            failures.ThrowIfAny();
        }

        static void Dispatch(Actor actor, float deltaTime, ref FailureCollector failures)
        {
            try
            {
                actor.DispatchTick(deltaTime);
            }
            catch (Exception e)
            {
                failures.Add(e);
            }
        }

        void FlushPending()
        {
            if (pendingAdd.Count > 0)
            {
                for (var i = 0; i < pendingAdd.Count; i++)
                {
                    var actor = pendingAdd[i];

                    // 追加を待つ間に登録解除された(同一フレームでAdd→Remove)
                    if (!actor.PrimaryActorTick.registered) { continue; }

                    Insert(actor);
                }

                pendingAdd.Clear();
            }

            Compact();
        }

        public void Clear()
        {
            actors.Clear();
            pendingAdd.Clear();
            hasRemoved = false;
        }
    }
}
