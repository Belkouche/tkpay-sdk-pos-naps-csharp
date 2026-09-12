#if !NET5_0_OR_GREATER
// Minimal source polyfills for System.Index and System.Range so C# 8 index/range
// syntax (a[..count], e[2..4], buf[..count]) compiles on .NET Framework 4.8.
// ReSharper disable once CheckNamespace
using System.Runtime.CompilerServices;

namespace System
{
    internal readonly struct Index : IEquatable<Index>
    {
        private readonly int _value;

        public Index(int value, bool fromEnd = false)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Value must be non-negative.");
            _value = fromEnd ? ~value : value;
        }

        public static Index Start => new Index(0);
        public static Index End => new Index(~0);
        public static Index FromStart(int value) => new Index(value);
        public static Index FromEnd(int value) => new Index(value, fromEnd: true);

        public int Value => _value < 0 ? ~_value : _value;
        public bool IsFromEnd => _value < 0;

        public int GetOffset(int length) => IsFromEnd ? _value + length + 1 : _value;

        public static implicit operator Index(int value) => FromStart(value);

        public bool Equals(Index other) => _value == other._value;
        public override bool Equals(object? obj) => obj is Index other && Equals(other);
        public override int GetHashCode() => _value;
    }

    internal readonly struct Range : IEquatable<Range>
    {
        public Index Start { get; }
        public Index End { get; }

        public Range(Index start, Index end)
        {
            Start = start;
            End = end;
        }

        public static Range StartAt(Index start) => new Range(start, Index.End);
        public static Range EndAt(Index end) => new Range(Index.Start, end);
        public static Range All => new Range(Index.Start, Index.End);

        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            var start = Start.GetOffset(length);
            var end = End.GetOffset(length);
            if ((uint)end > (uint)length || (uint)start > (uint)end)
                throw new ArgumentOutOfRangeException(nameof(length));
            return (start, end - start);
        }

        public bool Equals(Range other) => Start.Equals(other.Start) && End.Equals(other.End);
        public override bool Equals(object? obj) => obj is Range other && Equals(other);
        public override int GetHashCode() => Start.GetHashCode() * 31 + End.GetHashCode();
    }
}
#endif
