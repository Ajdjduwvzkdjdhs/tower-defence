namespace KakigoriTD.Core
{
    /// <summary>
    /// 状態遷移を駆動する有限状態機械（ADR-2）。純ロジック（ADR-8）— UnityEngine 非依存。
    /// </summary>
    public class GameStateMachine
    {
        /// <summary>現在の状態（未設定時は null）。</summary>
        public IGameState Current { get; private set; }

        /// <summary>
        /// 現状態を Exit してから新状態へ切り替え、Enter する。
        /// 現状態・次状態が null でも安全。
        /// </summary>
        public void Change(IGameState next)
        {
            Current?.Exit();
            Current = next;
            Current?.Enter();
        }

        /// <summary>現状態の Tick を呼ぶ。状態未設定でも安全。</summary>
        public void Tick(float deltaTime) => Current?.Tick(deltaTime);
    }
}
