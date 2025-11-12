using System.Collections.Concurrent;

namespace Tichu.Services
{
    public class TimerService
    {
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _timers = new();

        /// <summary>
        /// 턴 제한시간 시작
        /// </summary>
        public async Task StartTurnTimerAsync(int roomId, int seconds, Func<Task> onTimeout)
        {
            if (_timers.TryGetValue(roomId, out var existing))
            {
                existing.Cancel();
            }

            var cts = new CancellationTokenSource();
            _timers[roomId] = cts;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token);
                await onTimeout();
            }
            catch (TaskCanceledException)
            {
                // 턴이 정상 종료된 경우
            }
        }

        /// <summary>
        /// 턴 종료 시 타이머 정지
        /// </summary>
        public void StopTimer(int roomId)
        {
            if (_timers.TryRemove(roomId, out var cts))
            {
                cts.Cancel();
            }
        }
    }
}