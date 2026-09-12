#if !NET5_0_OR_GREATER
// Polyfill for init-only setters on .NET Framework 4.8 and earlier.
// The compiler requires this type to exist when using 'init' accessors with C# 9+.
// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
