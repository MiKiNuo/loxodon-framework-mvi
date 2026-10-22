using System;
using System.Collections.Generic;
using System.Linq;
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
                if (!decision.IsConfigured)
                {
                    decision = _defaultDecision;
                }

                if (!decision.Trace.IsConfigured)
                {
                    decision = decision.WithTrace(new MviErrorDecisionTrace(
                        ruleId: rule.RuleId,
                        priority: rule.Priority,
                        phase: context.Phase,
                        attempt: context.Attempt,
                        note: rule.Note,
                        isMatched: true));
                }

                return new ValueTask<MviErrorDecision>(decision);
            }

            var fallbackDecision = _defaultDecision;
            if (!fallbackDecision.Trace.IsConfigured)
            {
                fallbackDecision = fallbackDecision.WithTrace(new MviErrorDecisionTrace(
                    ruleId: "default",
                    priority: int.MaxValue,
                    phase: context.Phase,
                    attempt: context.Attempt,
                    note: "default-decision",
                    isMatched: false));
            }

            return new ValueTask<MviErrorDecision>(fallbackDecision);
        }

        internal readonly struct Rule
        {
            public Rule(
                string ruleId,
                int priority,
                int order,
                string note,
                Func<MviErrorContext, bool> match,
                Func<MviErrorContext, MviErrorDecision> decide)
            {
                RuleId = string.IsNullOrWhiteSpace(ruleId) ? "rule" : ruleId;
                Priority = priority;
                Order = order;
                Note = note ?? string.Empty;
                Match = match ?? throw new ArgumentNullException(nameof(match));
                Decide = decide ?? throw new ArgumentNullException(nameof(decide));
            }

            public string RuleId { get; }

            public int Priority { get; }

            public int Order { get; }

            public string Note { get; }

            public Func<MviErrorContext, bool> Match { get; }

            public Func<MviErrorContext, MviErrorDecision> Decide { get; }
        }
    }

    /// <summary>
    /// 模板错误策略构建器（支持异常类型、业务码与指数退避）。
    /// </summary>
    public sealed class TemplateMviErrorStrategyBuilder
    {
        public const int DefaultPriority = 1000;

        private static readonly object JitterRandomSyncRoot = new();
        private static readonly Random JitterRandom = new();
        private readonly List<TemplateMviErrorStrategy.Rule> _rules = new();
        private MviErrorDecision _defaultDecision = MviErrorDecision.Emit();
        private int _ruleOrder;

        public TemplateMviErrorStrategyBuilder UseDefaultDecision(MviErrorDecision decision)
        {
            _defaultDecision = decision.IsConfigured ? decision : MviErrorDecision.Emit();
            return this;
        }

        public TemplateMviErrorStrategyBuilder ForException<TException>(
            Func<MviErrorContext, MviErrorDecision> decision,
            string ruleId = null,
            int priority = DefaultPriority)
            where TException : Exception
        {
            return AddRule(
                ruleId: ResolveRuleId(ruleId, $"exception:{typeof(TException).Name}"),
                priority: priority,
                note: $"Exception={typeof(TException).Name}",
                match: context => context.Exception is TException,
                decide: decision);
        }

        public TemplateMviErrorStrategyBuilder ForExceptionInPhase<TException>(
            MviErrorPhase phase,
            Func<MviErrorContext, MviErrorDecision> decision,
            string ruleId = null,
            int priority = DefaultPriority)
            where TException : Exception
        {
            return AddRule(
                ruleId: ResolveRuleId(ruleId, $"exception:{typeof(TException).Name}:phase:{phase}"),
                priority: priority,
                note: $"Exception={typeof(TException).Name},Phase={phase}",
                match: context => context.Phase == phase && context.Exception is TException,
                decide: decision);
        }

        public TemplateMviErrorStrategyBuilder ForBusinessCode(
            int businessCode,
            Func<MviErrorContext, MviErrorDecision> decision,
            string ruleId = null,
            int priority = DefaultPriority)
        {
            return AddRule(
                ruleId: ResolveRuleId(ruleId, $"business:{businessCode}"),
                priority: priority,
                note: $"BusinessCode={businessCode}",
                match: context => context.Exception is IBusinessCodeException codeException && codeException.BusinessCode == businessCode,
                decide: decision);
        }

        public TemplateMviErrorStrategyBuilder ForBusinessCodeInPhase(
            int businessCode,
            MviErrorPhase phase,
            Func<MviErrorContext, MviErrorDecision> decision,
            string ruleId = null,
            int priority = DefaultPriority)
        {
            return AddRule(
                ruleId: ResolveRuleId(ruleId, $"business:{businessCode}:phase:{phase}"),
                priority: priority,
                note: $"BusinessCode={businessCode},Phase={phase}",
                match: context => context.Phase == phase
                                  && context.Exception is IBusinessCodeException codeException
                                  && codeException.BusinessCode == businessCode,
                decide: decision);
        }

        public TemplateMviErrorStrategyBuilder ForPhase(
            MviErrorPhase phase,
            Func<MviErrorContext, MviErrorDecision> decision,
            string ruleId = null,
            int priority = DefaultPriority)
        {
            return AddRule(
                ruleId: ResolveRuleId(ruleId, $"phase:{phase}"),
                priority: priority,
                note: $"Phase={phase}",
                match: context => context.Phase == phase,
                decide: decision);
        }

        public TemplateMviErrorStrategyBuilder ForPredicate(
            Func<MviErrorContext, bool> predicate,
            Func<MviErrorContext, MviErrorDecision> decision,
            string ruleId = null,
            int priority = DefaultPriority)
        {
            return AddRule(
                ruleId: ResolveRuleId(ruleId, "predicate"),
                priority: priority,
                note: "CustomPredicate",
                match: predicate,
                decide: decision);
        }

        public TemplateMviErrorStrategyBuilder UseExponentialBackoffForException<TException>(
            int maxRetryCount,
            int baseDelayMs,
            int maxDelayMs = 5000,
            bool emitErrorOnRetry = false,
            MviErrorDecision? exhaustedDecision = null,
            string ruleId = null,
            int priority = DefaultPriority)
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
            }, ruleId: ruleId, priority: priority);
        }

        public TemplateMviErrorStrategyBuilder UseExponentialBackoffForExceptionInPhase<TException>(
            MviErrorPhase phase,
            int maxRetryCount,
            int baseDelayMs,
            int maxDelayMs = 5000,
            bool emitErrorOnRetry = false,
            MviErrorDecision? exhaustedDecision = null,
            string ruleId = null,
            int priority = DefaultPriority)
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
            }, ruleId: ruleId, priority: priority);
        }

        public TemplateMviErrorStrategyBuilder UseExponentialBackoffForExceptionWithJitter<TException>(
            int maxRetryCount,
            int baseDelayMs,
            int maxDelayMs = 5000,
            double jitterRate = 0.2d,
            bool emitErrorOnRetry = false,
            MviErrorDecision? exhaustedDecision = null,
            string ruleId = null,
            int priority = DefaultPriority)
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
            }, ruleId: ruleId, priority: priority);
        }

        public TemplateMviErrorStrategyBuilder UseExponentialBackoffForExceptionInPhaseWithJitter<TException>(
            MviErrorPhase phase,
            int maxRetryCount,
            int baseDelayMs,
            int maxDelayMs = 5000,
            double jitterRate = 0.2d,
            bool emitErrorOnRetry = false,
            MviErrorDecision? exhaustedDecision = null,
            string ruleId = null,
            int priority = DefaultPriority)
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
            }, ruleId: ruleId, priority: priority);
        }

        public TemplateMviErrorStrategy Build()
        {
            var sortedRules = _rules
                .OrderBy(rule => rule.Priority)
                .ThenBy(rule => rule.Order)
                .ToArray();
            return new TemplateMviErrorStrategy(sortedRules, _defaultDecision);
        }

        private TemplateMviErrorStrategyBuilder AddRule(
            string ruleId,
            int priority,
            string note,
            Func<MviErrorContext, bool> match,
            Func<MviErrorContext, MviErrorDecision> decide)
        {
            if (match == null || decide == null)
            {
                return this;
            }

            _ruleOrder++;
            _rules.Add(new TemplateMviErrorStrategy.Rule(
                ruleId: ruleId,
                priority: priority,
                order: _ruleOrder,
                note: note,
                match: match,
                decide: decide));
            return this;
        }

        private static string ResolveRuleId(string inputRuleId, string fallback)
        {
            return string.IsNullOrWhiteSpace(inputRuleId)
                ? $"auto:{fallback}"
                : inputRuleId;
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
    }
}
