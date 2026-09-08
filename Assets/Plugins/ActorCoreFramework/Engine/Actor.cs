using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace ActorCoreFramework
{
    /// <summary>
    /// Worldに所有される実体。生成はコンストラクタ、破棄はWorld.Destroyが担う。
    ///
    /// コンストラクタは組み立てだけに留め、後始末が必要になる操作はOnBeginPlayで行い、
    /// OnEndPlayで戻すこと。イベント購読、InputActionのEnable、プールからの借用、
    /// 他オブジェクトへの自己登録などが該当する。参照を受け取って保持するだけなら、
    /// 対になる解放が不要なのでコンストラクタで構わない。
    ///
    /// Worldに登録されなかったActorにはBeginPlay/EndPlayが一度も配送されないため、
    /// この規約を破ると解放される機会のない状態が生まれる。
    /// </summary>
    public abstract class Actor
    {
        /// <summary>
        /// Idの発番元。Domain Reloadを無効にしても意図的にリセットしない。
        /// リセットすると前回の再生から生き残ったActorとIdが衝突しうる。
        /// </summary>
        static int s_nextId;

        readonly List<ActorComponent> components = new();

        string? name;
        bool ticking;
        bool hasPendingRemoval;

        /// <summary>
        /// プロセス内で一意な識別子。生成順に1から発番される。
        /// ログの突き合わせなど、同名のActorを区別したい場面で使う。
        /// </summary>
        public int Id { get; } = Interlocked.Increment(ref s_nextId);

        /// <summary>
        /// デバッグ表示用の名前。既定は型名。
        /// nullや空文字を代入すると既定へ戻る。UEのAActor::GetName()相当。
        /// </summary>
        public string Name
        {
            get => name ??= GetType().Name;
            set => name = string.IsNullOrEmpty(value) ? null : value;
        }

        public ActorState State { get; private set; } = ActorState.Created;
        public World? World { get; private set; }
        public TickFunction PrimaryActorTick { get; } = new();

        public bool IsPlaying => State == ActorState.Playing;

        /// <summary>
        /// World.Destroyで破棄が予約されている間true。
        /// 実際の破棄はPostTickの末尾まで遅延するため、その間もStateはPlayingのまま。
        /// 破棄予定のActorへ処理を積みたくない場合はこのフラグを見ること。
        /// </summary>
        public bool IsPendingDestroy { get; internal set; }

        /// <summary>
        /// 合成されているComponent。Tick中に取り外しを予約したComponentは、
        /// 実際に除去されるまでの間ここに残る。IsPendingRemovalで判別できる。
        /// </summary>
        public IReadOnlyList<ActorComponent> Components => components;


        // --- Component ---

        /// <summary>
        /// Componentを合成する。通常はコンストラクタで呼ぶ。
        /// BeginPlay後に追加した場合は、その場でBeginPlayが配送される。
        /// </summary>
        protected T AddComponent<T>(T component) where T : ActorComponent
        {
            if (component == null) { throw new ArgumentNullException(nameof(component)); }

            if (State is ActorState.Ended or ActorState.Disposed)
            {
                throw new InvalidOperationException($"Cannot add a component to a {State} actor.");
            }

            // 二重合成や他Actorとの共有を弾く。失敗時に中途半端な状態を残さないよう、
            // リストへ入れる前に所有権を確定させる。
            component.Attach(this);
            components.Add(component);

            if (State == ActorState.Playing)
            {
                component.DispatchBeginPlay();
            }

            return component;
        }

        /// <summary>
        /// Componentを取り外して破棄する。Actorが再生中なら、その場でEndPlayが配送される。
        /// Tick中に呼んだ場合、実際の除去はそのTickが終わってから行われる。
        /// </summary>
        /// <returns>取り外しを受け付けたならtrue。所有者が違う、または予約済みならfalse。</returns>
        protected bool RemoveComponent(ActorComponent component)
        {
            if (component == null) { return false; }
            if (!ReferenceEquals(component.Owner, this)) { return false; }
            if (component.IsPendingRemoval) { return false; }

            component.IsPendingRemoval = true;
            hasPendingRemoval = true;

            // Tick中はDispatchTickの添字が飛ばないよう、除去をTickの完了まで遅らせる
            if (!ticking) { FlushPendingRemoval(); }

            return true;
        }

        void FlushPendingRemoval()
        {
            // 除去に伴うEndPlayから更にRemoveComponentが呼ばれても取りこぼさない
            while (hasPendingRemoval)
            {
                hasPendingRemoval = false;

                for (var i = components.Count - 1; i >= 0; i--)
                {
                    var component = components[i];
                    if (!component.IsPendingRemoval) { continue; }

                    components.RemoveAt(i);

                    // BeginPlayを受け取っているかはComponent自身が持つ。
                    // 所有者のEndPlay中に取り外された場合でも対のEndPlayが届く。
                    component.DispatchEndPlay(EndPlayReason.Destroyed);
                    component.Dispose();
                }
            }
        }

        /// <summary>
        /// 指定した型のComponentをすべて集める。
        /// 呼び出し側のリストへ詰めるので、リストを使い回せば毎フレーム呼んでも
        /// アロケーションが発生しない。取り外しが予約されたComponentは対象外。
        /// </summary>
        /// <param name="results">結果の格納先。呼び出しごとにクリアされる。</param>
        /// <returns>集めた件数。</returns>
        public int GetComponents<T>(List<T> results) where T : ActorComponent
        {
            if (results == null) { throw new ArgumentNullException(nameof(results)); }

            results.Clear();

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component.IsPendingRemoval) { continue; }

                if (component is T found) { results.Add(found); }
            }

            return results.Count;
        }

        public bool TryGetComponent<T>([NotNullWhen(true)] out T? component) where T : ActorComponent
        {
            for (var i = 0; i < components.Count; i++)
            {
                // 取り外しが予約されたComponentは、もう見つからないものとして扱う
                if (components[i].IsPendingRemoval) { continue; }

                if (components[i] is T found)
                {
                    component = found;
                    return true;
                }
            }

            component = null;
            return false;
        }


        // --- Worldからのみ駆動される ---

        internal void DispatchBeginPlay(World world)
        {
            if (State != ActorState.Created) { return; }

            World = world;
            State = ActorState.Playing;

            // 派生クラスがbase呼び出しを忘れても壊れないよう、
            // フレームワーク内部の処理は非virtualなここで行う。
            OnInternalBeginPlay();

            for (var i = 0; i < components.Count; i++)
            {
                components[i].DispatchBeginPlay();
            }

            OnBeginPlay();
        }

        internal void DispatchEndPlay(EndPlayReason reason)
        {
            if (State != ActorState.Playing) { return; }

            State = ActorState.Ended;
            OnEndPlay(reason);

            // 合成と逆順に解除する
            for (var i = components.Count - 1; i >= 0; i--)
            {
                components[i].DispatchEndPlay(reason);
            }

            OnInternalEndPlay(reason);
        }

        internal void DispatchTick(float deltaTime)
        {
            if (State != ActorState.Playing) { return; }

            ticking = true;

            try
            {
                // Tick中にComponentが増減してもよいよう添字で回す。
                // 追加分は同じTickで配送され、除去分はTickの完了後にまとめて反映される。
                for (var i = 0; i < components.Count; i++)
                {
                    var component = components[i];
                    if (component.IsPendingRemoval) { continue; }

                    component.DispatchTick(deltaTime);
                }

                OnTick(deltaTime);
            }
            finally
            {
                // Tickが例外で抜けても、予約された除去は必ず反映してから戻る
                ticking = false;
                FlushPendingRemoval();
            }
        }


        /// <summary>
        /// フレームワーク内部の初期化。派生クラスのOnBeginPlayより先に呼ばれる。
        /// アセンブリ外からはオーバーライドできない。
        /// </summary>
        internal virtual void OnInternalBeginPlay() { }

        /// <summary>
        /// フレームワーク内部の後始末。派生クラスのOnEndPlayより後に呼ばれる。
        /// </summary>
        internal virtual void OnInternalEndPlay(EndPlayReason reason) { }


        // --- 派生クラスの実装ポイント ---

        protected virtual void OnBeginPlay() { }
        protected virtual void OnEndPlay(EndPlayReason reason) { }
        protected virtual void OnTick(float deltaTime) { }
        protected virtual void OnDispose() { }


        /// <summary>
        /// Worldからのみ呼ばれる。外部から破棄するにはWorld.Destroyを使うこと。
        /// 破棄経路を一本化することで、Worldの管理下にDisposeされたActorが残らないようにする。
        /// </summary>
        internal void Dispose()
        {
            if (State == ActorState.Disposed) { return; }

            // World.Dispose経由など、EndPlayを経ていない場合に備える
            if (State == ActorState.Playing)
            {
                DispatchEndPlay(EndPlayReason.Destroyed);
            }

            State = ActorState.Disposed;

            for (var i = components.Count - 1; i >= 0; i--)
            {
                components[i].Dispose();
            }
            components.Clear();

            OnDispose();

            // ComponentやOnDisposeからWorldを参照できるよう、参照を切るのは最後
            World = null;
        }


        public override string ToString() => $"{Name}#{Id}";
    }
}
