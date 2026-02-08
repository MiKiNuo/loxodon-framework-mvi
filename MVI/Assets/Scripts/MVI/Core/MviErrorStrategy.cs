using System;
using System.Threading;
using System.Threading.Tasks;

namespace MVI
{
    public enum MviErrorPhase
    {
        Unknown = 0,
        IntentProcessing = 1,
        Replay = 2,
        Reducing = 3,
        PersistenceLoad = 4,
        PersistenceSave = 5
    }

    public sealed class MviErrorContext
    {
        public MviErrorContext(Store store, Exception exception, IIntent intent, int attempt, MviErrorPhase phase)
        {
            Store = store;
            Exception = exception;
            Intent = intent;
            Attempt = attempt;
            Phase = phase;
        }

        public Store Store { get; }

        public Exception Exception { get; }

        public IIntent Intent { get; }

        public int Attempt { get; }

        public MviErrorPhase Phase { get; }
    }

    public readonly struct MviErrorDecision
    {
        public MviErrorDecision(bool emitError, bool rethrow, int retryCount, TimeSpan retryDelay, IMviResult fallbackResult)
        {
            EmitError = emitError;
            Rethrow = rethrow;
            RetryCount = retryCount < 0 ? 0 : retryCount;
            RetryDelay = retryDelay;
            FallbackResult = fallbackResult;
            IsConfigured = true;
        }

        public bool EmitError { get; }

        public bool Rethrow { get; }

        public int RetryCount { get; }

        public TimeSpan RetryDelay { get; }

        public IMviResult FallbackResult { get; }

        public bool IsConfigured { get; }

        public static MviErrorDecision Emit(bool rethrow = false)
        {
            return new MviErrorDecision(emitError: true, rethrow: rethrow, retryCount: 0, retryDelay: default, fallbackResult: null);
        }

        public static MviErrorDecision Ignore()
        {
            return new MviErrorDecision(emitError: false, rethrow: false, retryCount: 0, retryDelay: default, fallbackResult: null);
        }

        public static MviErrorDecision Retry(int retryCount, TimeSpan retryDelay = default, bool emitError = true)
        {
            return new MviErrorDecision(emitError: emitError, rethrow: false, retryCount: retryCount, retryDelay: retryDelay, fallbackResult: null);
        }

        public static MviErrorDecision Fallback(IMviResult fallbackResult, bool emitError = true)
        {
            return new MviErrorDecision(emitError: emitError, rethrow: false, retryCount: 0, retryDelay: default, fallbackResult: fallbackResult);
        }
    }

    public interface IMviErrorStrategy
    {
        ValueTask<MviErrorDecision> DecideAsync(MviErrorContext context, CancellationToken cancellationToken = default);
    }

    public sealed class DefaultMviErrorStrategy : IMviErrorStrategy
    {
        public static readonly DefaultMviErrorStrategy Instance = new();

        private DefaultMviErrorStrategy()
        {
        }

        public ValueTask<MviErrorDecision> DecideAsync(MviErrorContext context, CancellationToken cancellationToken = default)
        {
            return new ValueTask<MviErrorDecision>(MviErrorDecision.Emit());
        }
    }
}
