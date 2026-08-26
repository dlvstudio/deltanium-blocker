using System.Threading;

namespace Deltanium.Blocker.Services.Utils
{
    public class MetricsService
    {
        private long _txAccepted;
        private long _txRejected;
        private long _txInvalid;

        public void IncrementAccepted() => Interlocked.Increment(ref _txAccepted);
        public void IncrementRejected() => Interlocked.Increment(ref _txRejected);
        public void IncrementInvalid() => Interlocked.Increment(ref _txInvalid);

        public object Snapshot() => new
        {
            txAccepted = Interlocked.Read(ref _txAccepted),
            txRejected = Interlocked.Read(ref _txRejected),
            txInvalid = Interlocked.Read(ref _txInvalid)
        };
    }
}
