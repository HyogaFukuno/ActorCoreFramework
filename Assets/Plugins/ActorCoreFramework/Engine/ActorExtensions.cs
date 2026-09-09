using System;
using System.Threading;

namespace ActorCoreFramework
{
    public static class ActorExtensions
    {
        /// <summary>
        /// Actorの寿命を、渡したCancellationTokenの寿命に紐づける。
        /// トークンがキャンセルされた時点でWorld.Destroyが予約される。
        ///
        /// ActorはGameObjectの寿命から独立しているため、GameObjectを壊しても
        /// Worldは自動では気付かない。MonoBehaviourのdestroyCancellationTokenを
        /// 渡すことで、その2つを結び直せる。
        /// <code>
        /// var pawn = world.Spawn(() => new PlayerCharacter(transform, rigidbody, collider))
        ///                 .BindTo(destroyCancellationToken);
        /// </code>
        ///
        /// 紐づけ先はMonoBehaviourに限らない。シーンのアンロードや、
        /// 他のActorのDestroyTokenを渡せば「親が死んだら子も畳む」も同じ形で書ける。
        ///
        /// 購読はBeginPlayで始まり、EndPlayで解除される。BeginPlay前に呼んだ場合は
        /// 登録時まで待つので、Worldへ登録しないまま購読が残ることはない。
        /// 破棄が予約された時点でTickの配送は止まるため、
        /// 壊れたGameObjectを掴んだままTickされることはない。
        /// </summary>
        /// <returns>紐づけたActor自身。生成にそのまま繋げられるようにするため。</returns>
        /// <exception cref="ArgumentNullException">actorがnull。</exception>
        /// <exception cref="InvalidOperationException">
        /// 既に紐づけ済みか、EndPlayを終えたActorを紐づけようとした。
        /// 複数の寿命に紐づけたい場合はCreateLinkedTokenSourceでまとめること。
        /// </exception>
        public static T BindTo<T>(this T actor, CancellationToken token) where T : Actor
        {
            if (actor == null) { throw new ArgumentNullException(nameof(actor)); }

            actor.BindToLifetime(token);

            return actor;
        }
    }
}
