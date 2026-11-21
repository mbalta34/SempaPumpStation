using System;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>
    /// When the PLC link is tried again after it failed: in rounds of <see cref="AttemptsPerRound"/> attempts
    /// <see cref="AttemptSpacing"/> apart, a new round every <see cref="RoundInterval"/> (config.xml ReconnectAttempts,
    /// ReconnectIntervalSeconds; default 3 attempts every 30 s), for as long as the PLC stays unreachable.
    /// Pure timing, so the smoke test can check it without a PLC.
    /// </summary>
    public sealed class ReconnectPolicy
    {
        public int AttemptsPerRound { get; }
        public TimeSpan RoundInterval { get; }
        public TimeSpan AttemptSpacing { get; }

        private DateTime _roundStart, _next = DateTime.MinValue;

        /// <summary>Attempts made in the current round (0 before the first one).</summary>
        public int Attempt { get; private set; }
        /// <summary>When the next attempt is due; <see cref="DateTime.MinValue"/> = now.</summary>
        public DateTime NextAttempt => _next;
        /// <summary>Every attempt of the last round failed: waiting for the next round.</summary>
        public bool WaitingForNextRound { get; private set; }

        public ReconnectPolicy(int attemptsPerRound = 3, int roundIntervalSeconds = 30)
        {
            AttemptsPerRound = Math.Max(1, attemptsPerRound);
            RoundInterval = TimeSpan.FromSeconds(Math.Max(1, roundIntervalSeconds));
            // The attempts of a round spread over its first third (5 s apart in a 30 s round: at 0, 5 and 10 s), at least 2 s apart
            double spacing = AttemptsPerRound > 1 ? RoundInterval.TotalSeconds / 3.0 / (AttemptsPerRound - 1) : 0;
            AttemptSpacing = TimeSpan.FromSeconds(AttemptsPerRound > 1 ? Math.Max(2.0, spacing) : 0);
        }

        public bool Due(DateTime now) => now >= _next;

        /// <summary>An attempt is starting now: counts it, and starts a round with the first one.</summary>
        public void Starting(DateTime now)
        {
            if (Attempt == 0 || WaitingForNextRound)
            {
                Attempt = 0;
                WaitingForNextRound = false;
                _roundStart = now;
            }
            Attempt++;
        }

        /// <summary>The attempt failed: the next one later in this round, or at the start of the next round.</summary>
        public void Failed(DateTime now)
        {
            if (Attempt < AttemptsPerRound)
                _next = now + AttemptSpacing;
            else
            {
                WaitingForNextRound = true;
                _next = _roundStart + RoundInterval;
                if (_next < now) _next = now;
            }
        }

        /// <summary>Connected: the next failure starts a new round at once.</summary>
        public void Reset()
        {
            Attempt = 0;
            WaitingForNextRound = false;
            _next = DateTime.MinValue;
        }
    }
}
