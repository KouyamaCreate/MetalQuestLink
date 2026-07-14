using System;
using System.Diagnostics;
using System.Threading;

namespace MaQuestLink.QuestClient
{
    /// <summary>Ping/Pongの往復中央時刻からMac monotonic clockとQuest clockの差を推定する。</summary>
    public sealed class ClockSynchronizer
    {
        private static readonly double NanosecondsPerTick = 1_000_000_000.0 / Stopwatch.Frequency;
        private long hostMinusClientNs;
        private long roundTripNs;
        private int synchronized;

        public bool IsSynchronized => Volatile.Read(ref synchronized) != 0;
        public long HostMinusClientNs => Interlocked.Read(ref hostMinusClientNs);
        public double RoundTripMs => Interlocked.Read(ref roundTripNs) / 1_000_000.0;

        public static ulong NowNs()
        {
            return unchecked((ulong)(Stopwatch.GetTimestamp() * NanosecondsPerTick));
        }

        public void Update(ulong clientSendNs, ulong hostReceiveNs, ulong clientReceiveNs)
        {
            if (clientReceiveNs < clientSendNs)
            {
                return;
            }
            var midpoint = clientSendNs + (clientReceiveNs - clientSendNs) / 2ul;
            var offset = unchecked((long)hostReceiveNs - (long)midpoint);
            Interlocked.Exchange(ref hostMinusClientNs, offset);
            Interlocked.Exchange(ref roundTripNs, unchecked((long)(clientReceiveNs - clientSendNs)));
            Volatile.Write(ref synchronized, 1);
        }

        public double HostAgeMs(ulong hostTimestampNs, ulong clientTimestampNs)
        {
            if (!IsSynchronized)
            {
                return -1.0;
            }
            var hostTimestampInClientClock = unchecked((long)hostTimestampNs - HostMinusClientNs);
            var age = unchecked((long)clientTimestampNs - hostTimestampInClientClock);
            return Math.Max(0.0, age / 1_000_000.0);
        }
    }
}
