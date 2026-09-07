using System;

namespace ActorCoreFramework
{
    /// <summary>
    /// Tickの実行タイミング。列挙子の値はWorldが持つグループ配列の添字であり、
    /// 実行順ではない。1フレームの実行順は
    /// FixedTick(物理ステップごと) → Tick → UnscaledTick → PostTick。
    /// </summary>
    public enum TickGroup
    {
        Tick = 0,          // UnityのUpdate相当
        FixedTick = 1,     // FixedUpdate相当
        PostTick = 2,      // LateUpdate相当

        /// <summary>
        /// Tickの直後に、Time.timeScaleの影響を受けないdeltaTimeで回る。
        /// ポーズ中も動かしたいUIや演出に使う。
        /// </summary>
        UnscaledTick = 3,
    }

    public sealed class TickFunction
    {
        bool canEverTick;
        TickGroup group = TickGroup.Tick;
        int priority;

        /// <summary>
        /// Tickを使うかどうかの有無。コンストラクタで確定させる。
        /// World登録後の変更は登録状態に反映できないため、例外になる。
        /// 実行時の切り替えにはEnabledを使うこと。
        /// </summary>
        /// <exception cref="InvalidOperationException">World登録後に変更した。</exception>
        public bool CanEverTick
        {
            get => canEverTick;
            set
            {
                ThrowIfLocked(nameof(CanEverTick));
                canEverTick = value;
            }
        }

        /// <summary>
        /// 実行時の一時停止用フラグ。CanEverTickが無効なら無意味。
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 所属するTickグループ。CanEverTickと同様、コンストラクタで確定させる。
        /// </summary>
        /// <exception cref="InvalidOperationException">World登録後に変更した。</exception>
        public TickGroup Group
        {
            get => group;
            set
            {
                ThrowIfLocked(nameof(Group));
                group = value;
            }
        }

        /// <summary>
        /// 0なら毎フレーム。0.1なら0.1秒ごとにTickする。
        /// </summary>
        public float Interval { get; set; }

        /// <summary>
        /// 同一TickGroup内の実行順。小さいほど先に実行される。
        /// 同値ならWorldへの登録順を保つ。
        /// GroupやCanEverTickと同様、登録時点の値で並びが決まるため
        /// コンストラクタで確定させること。
        ///
        /// 例: Controllerが操作対象のPawnより先に入力を積みたい場合、
        /// ControllerのPriorityをPawnより小さくする。
        /// </summary>
        /// <exception cref="InvalidOperationException">World登録後に変更した。</exception>
        public int Priority
        {
            get => priority;
            set
            {
                ThrowIfLocked(nameof(Priority));
                priority = value;
            }
        }

        internal float accumulator;
        internal bool registered;

        /// <summary>
        /// Worldへの登録が済むとtrue。以降、登録時点で確定する設定は変更できない。
        /// CanEverTickがfalseで登録されたActorも対象にするため、
        /// Tickグループへ実際に登録されたかを示すregisteredとは別に持つ。
        /// </summary>
        internal bool locked;

        /// <summary>
        /// 登録時点のGroup。登録後にGroupが変更されても
        /// 解除先を取り違えないよう、Worldが控えておく。
        /// </summary>
        internal TickGroup registeredGroup;


        void ThrowIfLocked(string propertyName)
        {
            if (!locked) { return; }

            throw new InvalidOperationException(
                $"{nameof(TickFunction)}.{propertyName} cannot be changed after the actor has been " +
                "registered to a World. Set it in the constructor. " +
                $"To pause ticking at runtime, use {nameof(Enabled)}.");
        }
    }
}
