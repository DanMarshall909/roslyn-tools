using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class PipedSymbolReaderTests
{
    [Fact]
    public void with_flat_json_containing_fqn_returns_true_and_value()
    {
        var ok = PipedSymbolReader.TryReadFullyQualifiedName(
            "{\"fullyQualifiedName\":\"RoslynTools.Core.WorkspaceLoader\"}", out var fqn);

        ok.ShouldBeTrue();
        fqn.ShouldBe("RoslynTools.Core.WorkspaceLoader");
    }

    [Fact]
    public void with_indented_find_refs_shaped_json_extracts_fqn()
    {
        // find-refs emits WriteIndented = true (multi-line) with extra fields.
        var payload =
            """
            {
              "symbolName": "WorkspaceLoader",
              "fullyQualifiedName": "RoslynTools.Core.WorkspaceLoader",
              "references": [
                { "filePath": "C:/x.cs", "line": 3, "lineText": "new WorkspaceLoader()" }
              ]
            }
            """;

        var ok = PipedSymbolReader.TryReadFullyQualifiedName(payload, out var fqn);

        ok.ShouldBeTrue();
        fqn.ShouldBe("RoslynTools.Core.WorkspaceLoader");
    }

    [Fact]
    public void with_missing_fqn_field_returns_false()
    {
        var ok = PipedSymbolReader.TryReadFullyQualifiedName(
            "{\"symbolName\":\"WorkspaceLoader\"}", out var fqn);

        ok.ShouldBeFalse();
        fqn.ShouldBeNull();
    }

    [Fact]
    public void with_empty_fqn_value_returns_false()
    {
        var ok = PipedSymbolReader.TryReadFullyQualifiedName(
            "{\"fullyQualifiedName\":\"\"}", out var fqn);

        ok.ShouldBeFalse();
        fqn.ShouldBeNull();
    }

    [Fact]
    public void with_malformed_json_returns_false_and_does_not_throw()
    {
        var ok = PipedSymbolReader.TryReadFullyQualifiedName("{not json", out var fqn);

        ok.ShouldBeFalse();
        fqn.ShouldBeNull();
    }

    [Fact]
    public void with_null_or_whitespace_payload_returns_false()
    {
        PipedSymbolReader.TryReadFullyQualifiedName(null, out var a).ShouldBeFalse();
        a.ShouldBeNull();

        PipedSymbolReader.TryReadFullyQualifiedName("   \n  ", out var b).ShouldBeFalse();
        b.ShouldBeNull();
    }

    [Fact]
    public void with_non_object_json_root_returns_false()
    {
        PipedSymbolReader.TryReadFullyQualifiedName("[1,2,3]", out var fqn).ShouldBeFalse();
        fqn.ShouldBeNull();
    }

    // ── ResolveAsync (positional precedence + redirect guard) ─────────────────────

    [Fact]
    public async Task resolve_with_empty_positional_and_piped_fqn_returns_piped()
    {
        var reader = new StringReader("{\"fullyQualifiedName\":\"TestApp.Foo\"}");

        var resolved = await PipedSymbolReader.ResolveAsync(null, inputRedirected: true, reader);

        resolved.ShouldBe("TestApp.Foo");
    }

    [Fact]
    public async Task resolve_with_positional_present_returns_positional_without_reading_stdin()
    {
        var reader = new ThrowingTextReader();

        var resolved = await PipedSymbolReader.ResolveAsync("TestApp.Bar", inputRedirected: true, reader);

        resolved.ShouldBe("TestApp.Bar");
    }

    [Fact]
    public async Task resolve_when_not_redirected_returns_positional_without_reading_stdin()
    {
        var reader = new ThrowingTextReader();

        var resolved = await PipedSymbolReader.ResolveAsync(null, inputRedirected: false, reader);

        resolved.ShouldBeNull();
    }

    [Fact]
    public async Task resolve_with_empty_positional_and_no_fqn_in_stdin_returns_positional()
    {
        var reader = new StringReader("{\"symbolName\":\"Foo\"}");

        var resolved = await PipedSymbolReader.ResolveAsync(null, inputRedirected: true, reader);

        resolved.ShouldBeNull();
    }

    private sealed class ThrowingTextReader : TextReader
    {
        public override int Read() => throw new ShouldAssertException("stdin should not be read");
        public override Task<string> ReadToEndAsync() => throw new ShouldAssertException("stdin should not be read");
        public override string ReadToEnd() => throw new ShouldAssertException("stdin should not be read");
    }
}
