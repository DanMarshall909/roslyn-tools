namespace RoslynTools.Core;

/// <summary>
///     Marker for inspect-category commands (read-only, single-symbol or single-file scope).
///     Enforced by <see cref="RoslynTools.App" /> static ctor and
///     <c>tests/RoslynTools.Tests/Architecture/CategoryInterfaceContracts.cs</c> to imply
///     <see cref="RtCommandInfo.Category" /> == <c>"inspect"</c>,
///     <see cref="RtCommandInfo.Mutates" /> == <c>false</c>,
///     <see cref="RtCommandInfo.JsonOutput" /> == <c>true</c>.
/// </summary>
internal interface IInspectCommand : IRtCommand;
