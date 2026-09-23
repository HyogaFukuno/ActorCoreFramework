using UnityEngine;

namespace ActorCoreFramework
{
    public abstract class Character : Pawn
    {
        public Rigidbody Rigidbody { get; }
        public Collider Collider { get; }

        /// <summary>
        /// 物理ステップ上の座標を返す。移動判定やAIなど、物理と整合させたい処理の基準。
        ///
        /// Rigidbodyの補間(Interpolate)はTransformにだけ反映され、この値には効かない。
        /// FixedUpdateの間隔でしか変わらないので、カメラ追従や描画位置の基準には
        /// 補間済みのTransform.positionを使うこと。
        /// </summary>
        public sealed override Vector3 Position => Rigidbody.position;

        protected Character(Transform transform, Rigidbody rigidbody, Collider collider) : base(transform)
        {
            Rigidbody = rigidbody;
            Collider = collider;
        }
    }
}