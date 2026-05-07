namespace RoslynTools.Core;

[Flags]
internal enum RtOutputContext
{
    None = 0,
    Diagnostic = 1 << 0,
    Result = 1 << 1,
    Diff = 1 << 2,
    Interactive = 1 << 3,
    Ui = 1 << 4
}
