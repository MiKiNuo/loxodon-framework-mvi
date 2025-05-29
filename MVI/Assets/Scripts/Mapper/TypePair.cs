using System;

namespace Mapper
{
    public readonly struct TypePair : IEquatable<TypePair>
    {
        public Type SourceType { get; }
        public Type TargetType { get; }

        public TypePair(Type sourceType, Type targetType)
        {
            SourceType = sourceType;
            TargetType = targetType;
        }

        public bool Equals(TypePair other) => 
            SourceType == other.SourceType && TargetType == other.TargetType;

        public override bool Equals(object obj) => obj is TypePair other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(SourceType, TargetType);
    }
}