namespace RoslynTools.Core;

/// <summary>
///     Marker for analyze-category commands that operate on a trace or external
///     artifact rather than a Roslyn solution (e.g. <see cref="RoslynTools.Commands.DebugCommand"/>,
///     which reads a JSONL LLM trace file). Solution-scoped architecture checks
///     skip implementers of this marker. Inherits from
///     <see cref="IAnalyzeCommand"/> so it picks up the read-only / JSON-output
///     contract transitively.
/// </summary>
internal interface ITraceAnalyzeCommand : IAnalyzeCommand { }
