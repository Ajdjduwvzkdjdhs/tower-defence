namespace KakigoriTD.Core
{
    /// <summary>
    /// ゲーム進行の 1 状態（ADR-2: State パターン）。純ロジック（ADR-8）。
    /// </summary>
    public interface IGameState
    {
        /// <summary>状態に入ったとき 1 回呼ばれる。</summary>
        void Enter();

        /// <summary>毎フレーム呼ばれる。<paramref name="deltaTime"/> は経過秒。</summary>
        void Tick(float deltaTime);

        /// <summary>状態を出るとき 1 回呼ばれる。</summary>
        void Exit();
    }
}
