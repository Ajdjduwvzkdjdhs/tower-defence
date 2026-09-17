namespace KakigoriTD.Core
{
    /// <summary>
    /// タイトルの開始入力後、戦闘開始前に挟む「3.2.1.スタート！」のカウントダウン。
    /// GameStateMachine の他 State と同じく純ロジック（ADR-8）。MonoBehaviour のコルーチンは使わず、
    /// Tick(deltaTime) の積算で時間管理する。表示演出・効果音は IGameContext 経由で GameManager に委譲する
    /// （数字はステップが変わるたびに <see cref="IGameContext.ShowCountdownStep"/> を呼ぶだけで、
    /// 見た目・SEの詳細はここでは関知しない）。
    /// </summary>
    public class CountdownState : IGameState
    {
        // "3" → "2" → "1" → "スタート！" の順。最後の要素だけ「開始」扱い（別SE・強調表示）。
        private static readonly string[] Steps = { "3", "2", "1", "スタート！" };
        private const float StepSec = 1.0f; // 各表示の秒数（プロジェクター投影を想定し、詰め込みすぎない間隔）

        private readonly GameStateMachine _machine;
        private readonly IGameContext _ctx;

        private int _stepIndex;
        private float _stepT;

        public CountdownState(GameStateMachine machine, IGameContext ctx)
        {
            _machine = machine;
            _ctx = ctx;
        }

        public void Enter()
        {
            _stepIndex = 0;
            _stepT = 0f;
            ShowCurrentStep();
        }

        public void Tick(float deltaTime)
        {
            _stepT += deltaTime;
            if (_stepT < StepSec) return;

            _stepT = 0f;
            _stepIndex++;

            if (_stepIndex >= Steps.Length)
            {
                _machine.Change(new BattleState(_machine, _ctx));
                return;
            }

            ShowCurrentStep();
        }

        public void Exit()
        {
            _ctx.ClearCountdown();
        }

        private void ShowCurrentStep()
        {
            bool isFinal = _stepIndex == Steps.Length - 1; // 「スタート！」のみ true
            _ctx.ShowCountdownStep(Steps[_stepIndex], isFinal);
        }
    }
}