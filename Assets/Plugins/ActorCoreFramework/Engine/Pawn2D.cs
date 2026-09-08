using UnityEngine;

namespace ActorCoreFramework
{
    public abstract class Pawn2D : Pawn
    {
        /// <summary>
        /// 2D平面上の座標。zを含まないため、Vector3への変換コストを避けたい場合に使う。
        /// 2DのPawnにおける座標の定義はこちらが基準で、Positionはここから導出される。
        /// </summary>
        public virtual Vector2 Position2D => Transform.position;

        /// <summary>
        /// Position2Dにzを補ったもの。2Dでもzをレイヤ順に使う場合があるので落とさない。
        /// Position2Dと乖離しないよう、派生クラスはPosition2Dだけを実装すること。
        /// </summary>
        public sealed override Vector3 Position
        {
            get
            {
                var position = Position2D;
                return new Vector3(position.x, position.y, Transform.position.z);
            }
        }

        protected Pawn2D(Transform transform) : base(transform)
        {
        }
    }
}
