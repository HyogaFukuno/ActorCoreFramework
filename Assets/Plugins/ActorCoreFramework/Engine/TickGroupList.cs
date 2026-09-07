using System.Collections.Generic;

namespace ActorCoreFramework
{
    internal sealed class TickGroupList
    {
        readonly struct PendingOp
        {
            public readonly Actor Actor;
            public readonly bool IsAdd;

            public PendingOp(Actor actor, bool isAdd)
            {
                Actor = actor;
                IsAdd = isAdd;
            }
        }

        readonly List<Actor> actors = new();

        /// <summary>
        /// 追加と削除を1本のキューで持ち、要求された順に適用する。
        /// 別々のリストに分けると、同一フレームでAdd→Removeされた際に
        /// 削除が先に処理されて取りこぼす。
        /// </summary>
        readonly List<PendingOp> pending = new();

        bool ticking;

        public void Add(Actor actor)
        {
            if (ticking)
            {
                pending.Add(new PendingOp(actor, true));
                return;
            }

            Insert(actor);
        }

        public void Remove(Actor actor)
        {
            if (ticking)
            {
                pending.Add(new PendingOp(actor, false));
                return;
            }

            actors.Remove(actor);
        }

        /// <summary>
        /// Priority昇順の位置へ挿入する。
        /// 同値の場合は既存の要素の後ろに入るため、登録順が保たれる。
        /// </summary>
        void Insert(Actor actor)
        {
            var priority = actor.PrimaryActorTick.Priority;
            var index = actors.Count;

            for (var i = 0; i < actors.Count; i++)
            {
                if (actors[i].PrimaryActorTick.Priority <= priority) { continue; }

                index = i;
                break;
            }

            actors.Insert(index, actor);
        }

        public void Tick(float deltaTime)
        {
            ticking = true;

            try
            {
                // 途中でCountが変わらない前提。Tick中の変化分はPendingへ追加される
                for (var i = 0; i < actors.Count; i++)
                {
                    var actor = actors[i];

                    // 破棄予約済みのActorには、実際の破棄を待たずにTickを止める
                    if (!actor.IsPlaying || actor.IsPendingDestroy)
                    {
                        continue;
                    }

                    var tick = actor.PrimaryActorTick;
                    if (!tick.Enabled)
                    {
                        continue;
                    }

                    if (tick.Interval <= 0.0f)
                    {
                        actor.DispatchTick(deltaTime);
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

                    actor.DispatchTick(elapsed); // 間引いた分のDeltaをまとめて渡す
                }
            }
            finally
            {
                // Tick中に例外が出ても、状態と保留分は必ず反映してから抜ける
                ticking = false;
                FlushPending();
            }
        }

        void FlushPending()
        {
            if (pending.Count <= 0) { return; }

            for (var i = 0; i < pending.Count; i++)
            {
                var op = pending[i];
                if (op.IsAdd)
                {
                    Insert(op.Actor);
                }
                else
                {
                    actors.Remove(op.Actor);
                }
            }

            pending.Clear();
        }

        public void Clear()
        {
            actors.Clear();
            pending.Clear();
        }
    }
}
