using System;
using R3;

namespace MVI
{
    // 指定某类 Intent 的并发处理策略。
    public readonly struct IntentProcessingPolicy
    {
        public IntentProcessingPolicy(AwaitOperation operation, int maxConcurrent = -1)
        {
            Operation = operation;
            MaxConcurrent = maxConcurrent;
        }

        public AwaitOperation Operation { get; }

        public int MaxConcurrent { get; }

        public static IntentProcessingPolicy Queue()
        {
            return new IntentProcessingPolicy(AwaitOperation.Sequential);
        }

        public static IntentProcessingPolicy Drop()
        {
            return new IntentProcessingPolicy(AwaitOperation.Drop);
        }

        public static IntentProcessingPolicy Switch()
        {
            return new IntentProcessingPolicy(AwaitOperation.Switch);
        }

        public static IntentProcessingPolicy Parallel(int maxConcurrent = -1)
        {
            return new IntentProcessingPolicy(AwaitOperation.Parallel, maxConcurrent);
        }

        public static IntentProcessingPolicy SequentialParallel(int maxConcurrent = -1)
        {
            return new IntentProcessingPolicy(AwaitOperation.SequentialParallel, maxConcurrent);
        }

        public static IntentProcessingPolicy ThrottleFirstLast()
        {
            return new IntentProcessingPolicy(AwaitOperation.ThrottleFirstLast);
        }

        public static IntentProcessingPolicy FromDefaults(AwaitOperation operation, int maxConcurrent)
        {
            return new IntentProcessingPolicy(operation, maxConcurrent);
        }
    }
}
