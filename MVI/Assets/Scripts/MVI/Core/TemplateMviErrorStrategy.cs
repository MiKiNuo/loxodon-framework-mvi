using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MVI
{
    /// <summary>
    /// 可选的业务码异常接口。
    /// </summary>
    public interface IBusinessCodeException
    {
        int BusinessCode { get; }
    }

    /// <summary>
    /// 业务码异常基类（便于错误策略按业务码匹配）。
    /// </summary>
    public class BusinessCodeException : Exception, IBusinessCodeException
    {
        public BusinessCodeException(int businessCode, string message, Exception innerException = null)
            : base(message, innerException)
        {
            BusinessCode = businessCode;
        }

        public int BusinessCode { get; }
    }

    /// <summary>
    /// 模板化错误策略：按规则匹配异常类型/业务码并输出统一决策。
    /// </summary>
    public sealed class TemplateMviErrorStrategy : IMviErrorStrategy
    {
        private readonly IReadOnlyList<Rule> _rules;
        private readonly MviErrorDecision _defaultDecision;

        internal TemplateMviErrorStrategy(IReadOnlyList<Rule> rules, MviErrorDecision defaultDecision)
        {
            _rules = rules ?? Array.Empty<Rule>();
            _defaultDecision = defaultDecision.IsConfigured ? defaultDecision : MviErrorDecision.Emit();
        }

        public ValueTask<MviErrorDecision> DecideAsync(MviErrorContext context, CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                return new ValueTask<MviErrorDecision>(_defaultDecision);
            }

            for (var i = 0; i < _rules.Count; i++)
            {
                var rule = _rules[i];
                if (!rule.Match(context))
                {
                    continue;
                }

                var decision = rule.Decide(context);
                return new ValueTask<MviErrorDecision>(decision.IsConfigured ? decision : _defaultDecision);
            }

            return new ValueTask<MviErrorDecision>(_defaultDecision);
        }

        internal readonly struct Rule
        {
            public Rule(Func<MviErrorContext, bool> match, Func<MviErrorContext, MviErrorDecision> decide)
            {
                Match = match ?? throw new ArgumentNullException(nameof(match));
                Decide = decide ?? throw new ArgumentNullException(nameof(decide));
            }

            public Func<MviErrorContext, bool> Match { get; }

            public Func<MviErrorContext, MviErrorDecision> Decide { get; }
        }
    }

    /// <summary>
    /// 模板错误策略构建器（支持异常类型、业务码与指数退避）。
    /// </summary>
    public sealed class TemplateMviErrorStrategyBuilder
    {
        private static readonly object JitterRandomSyncRoot = new();
        private static readonly Random JitterRandom = new();
        private readonly List<TemplateMviErrorStrategy.Rule> _rules = new();
        private MviErrorDecision _defaultDecision = MviErrorDecision.Emit();

        public TemplateMviErrorStrategyBuilder UseDefaultDecision(MviErrorDecision decision)
        {
            _defaultDecision = decision.IsConfigured ? decision : MviErrorDecision.Emit();
            return this;
        }

        public TemplateMviErrorStrategyBuilder ForException<TException>(Func<MviErrorContext, MviErrorDecision> decision)
            where TException : Exception
        {
            if (decision == null)
            {
                return this;
            }

            _rules.Add(new TemplateMviErrorStrategy.Rule(
                match: context => context.Exception is TException,
                decide: decision));
            return this;
        }

        public TemplateMviErrorStrategyBuilder ForExceptionInPhase<TException>(MviErrorPhase phase, Func<MviErrorContext, MviErrorDecision> decision)
            where TException : Exception
        {
            if (decision == null)
            {
                return this;
            }

            _rules.Add(new TemplateMviErrorStrategy.Rule(
                match: context => context.Phase == phase && context.Exception is TException,
                decide: decision));
            return this;
        }

        public TemplateMviErrorStrategyBuilder ForBusinessCode(int businessCode, Func<MviErrorContext, MviErrorDecision> decision)
        {
            if (decision == null)
            {
                return this;
            }

            _rules.Add(new TemplateMviErrorStrategy.Rule(
                match: context => context.Exception is IBusinessCodeException codeException && codeException.BusinessCode == businessCode,
                decide: decision));
            return this;
        }

        public TemplateMviErrorStrategyBuilder ForBusinessCodeInPhase(int businessCode, MviErrorPhase phase, Func<MviErrorContext, MviErrorDecision> decision)
        {
            if (decision == null)
            {
                return this;
            }

            _rules.Add(new TemplateMviErrorStrategy.Rule(
                match: context => context.Phase == phase
                                  && context.Exception is IBusinessCodeException codeException
                                  && codeException.BusinessCode == businessCode,
                decide: decision));
            return this;
        }

        public TemplateMviErrorStrategyBuilder ForPhase(MviErrorPhase phase, Func<MviErrorContext, MviErrorDecision> decision)
        {
            if (decision == null)
            {
                return this;
            }

            _rules.Add(new TemplateMviErrorStrategy.Rule(
                match: context => context.Phase == phase,
                decide: decision));
            return this;
        }

        public TemplateMviErrorStrategyBuilder ForPredicate(Func<MviErrorContext, bool> predicate, Func<MviErrorContext, MviErrorDecision> decision)
        {
            if (predicate == null || decision == null)
            {
                return this;
            }

            _rules.Add(new TemplateMviErrorStrategy.Rule(
                match: predicate,
                decide: decision));
            return this;
        }

        public TemplateMviErrorStrategyBuilder UseExponentialBackoffForException<TException>(
            int maxRetryCount,
            int baseDelayMs,
            int maxDelayMs = 5000,
            bool emitErrorOnRetry = false,
            MviErrorDecision? exhaustedDecision = null)
            where TException : Exception
        {
            var retryCount = Math.Max(0, maxRetryCount);
            var baseDelay = Math.Max(0, baseDelayMs);
            var maxDelay = Math.Max(baseDelay, maxDelayMs);
            var exhausted = exhaustedDecision ?? MviErrorDecision.Emit();

            return ForException<TException>(context =>
            {
                if (context.Attempt < retryCount)
                {
                    var exponent = Math.Min(30, context.Attempt);
                    var delayMs = baseDelay * (1 << exponent);
                    if (delayMs > maxDelay)
                    {
                        delayMs = maxDelay;
                    }

                    return MviErrorDecision.Retry(
                        retryCount: retryCount,
                        retryDelay: TimeSpan.FromMilliseconds(delayMs),
                        emitError: emitErrorOnRetry);
                }

                return exhausted;
            });
        }

        public TemplateMviErrorStrategyBuilder UseExponentialBackoffForExceptionInPhase<TException>(
            MviErrorPhase phase,
            int maxRetryCount,
            int baseDelayMs,
            int maxDelayMs = 5000,
            bool emitErrorOnRetry = false,
            MviErrorDecision? exhaustedDecision = null)
            where TException : Exception
        {
            var retryCount = Math.Max(0, maxRetryCount);
            var baseDelay = Math.Max(0, baseDelayMs);
            var maxDelay = Math.Max(baseDelay, maxDelayMs);
            var exhausted = exhaustedDecision ?? MviErrorDecision.Emit();

            return ForExceptionInPhase<TException>(phase, context =>
            {
                if (context.Attempt < retryCount)
                {
                    var exponent = Math.Min(30, context.Attempt);
                    var delayMs = baseDelay * (1 << exponent);
                    if (delayMs > maxDelay)
                    {
                        delayMs = maxDelay;
                    }

                    return MviErrorDecision.Retry(
                        retryCount: retryCount,
                        retryDelay: TimeSpan.FromMilliseconds(delayMs),
                        emitError: emitErrorOnRetry);
                }

                return exhausted;
            });
        }

        public TemplateMviErrorStrategyBuilder UseExponentialBackoffForExceptionWithJitter<TException>(
            int maxRetryCount,
            int baseDelayMs,
            int maxDelayMs = 5000,
            double jitterRate = 0.2d,
            bool emitErrorOnRetry = false,
            MviErrorDecision? exhaustedDecision = null)
            where TException : Exception
        {
            var retryCount = Math.Max(0, maxRetryCount);
            var baseDelay = Math.Max(0, baseDelayMs);
            var maxDelay = Math.Max(baseDelay, maxDelayMs);
            var normalizedJitterRate = Math.Max(0d, Math.Min(1d, jitterRate));
            var exhausted = exhaustedDecision ?? MviErrorDecision.Emit();

            return ForException<TException>(context =>
            {
                if (context.Attempt < retryCount)
                {
                    var exponent = Math.Min(30, context.Attempt);
                    var delayMs = baseDelay * (1 << exponent);
                    if (delayMs > maxDelay)
                    {
                        delayMs = maxDelay;
                    }

                    var jitteredDelayMs = normalizedJitterRate <= 0d
                        ? delayMs
                        : ApplyJitter(delayMs, normalizedJitterRate);

                    return MviErrorDecision.Retry(
                        retryCount: retryCount,
                        retryDelay: TimeSpan.FromMilliseconds(jitteredDelayMs),
                        emitError: emitErrorOnRetry);
                }

                return exhausted;
            });
        }

        public TemplateMviErrorStrategyBuilder UseExponentialBackoffForExceptionInPhaseWithJitter<TException>(
            MviErrorPhase phase,
            int maxRetryCount,
            int baseDelayMs,
            int maxDelayMs = 5000,
            double jitterRate = 0.2d,
            bool emitErrorOnRetry = false,
            MviErrorDecision? exhaustedDecision = null)
            where TException : Exception
        {
            var retryCount = Math.Max(0, maxRetryCount);
            var baseDelay = Math.Max(0, baseDelayMs);
            var maxDelay = Math.Max(baseDelay, maxDelayMs);
            var normalizedJitterRate = Math.Max(0d, Math.Min(1d, jitterRate));
            var exhausted = exhaustedDecision ?? MviErrorDecision.Emit();

            return ForExceptionInPhase<TException>(phase, context =>
            {
                if (context.Attempt < retryCount)
                {
                    var exponent = Math.Min(30, context.Attempt);
                    var delayMs = baseDelay * (1 << exponent);
                    if (delayMs > maxDelay)
                    {
                        delayMs = maxDelay;
                    }

                    var jitteredDelayMs = normalizedJitterRate <= 0d
                        ? delayMs
                        : ApplyJitter(delayMs, normalizedJitterRate);

                    return MviErrorDecision.Retry(
                        retryCount: retryCount,
                        retryDelay: TimeSpan.FromMilliseconds(jitteredDelayMs),
                        emitError: emitErrorOnRetry);
                }

                return exhausted;
            });
        }

        private static int ApplyJitter(int baseDelayMs, double jitterRate)
        {
            if (baseDelayMs <= 0 || jitterRate <= 0d)
            {
                return baseDelayMs;
            }

            double sample;
            lock (JitterRandomSyncRoot)
            {
                sample = JitterRandom.NextDouble() * 2d - 1d;
            }

            var jittered = baseDelayMs + baseDelayMs * jitterRate * sample;
            var normalized = (int)Math.Round(jittered, MidpointRounding.AwayFromZero);
            return Math.Max(0, normalized);
        }

        public TemplateMviErrorStrategy Build()
        {
            return new TemplateMviErrorStrategy(_rules.ToArray(), _defaultDecision);
        }
    }
}
