using System.Text;
using System.Text.Json;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services.Results;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class WorkspaceEvidenceOutputTests
{
    [Theory]
    [InlineData("Json")]
    [InlineData("Llm")]
    public void inspection_output_summarizes_large_workspace_evidence_without_hiding_partial_state(
        string format)
    {
        var previousFormat = RtContext.Format;
        var previousFullEvidence = RtContext.FullWorkspaceEvidence;
        var previousExitCode = Environment.ExitCode;
        using var console = new CapturingConsole();
        using var scope = RtConsole.UseConsole(console);
        try
        {
            RtContext.Format = Enum.Parse<OutputFormat>(format);
            RtContext.FullWorkspaceEvidence = false;

            RtResultRenderer.Write(RtResult<RtResultEnvelope<string>>.Ok(LargePartialOutline()));

            var output = console.Text;
            Encoding.UTF8.GetByteCount(output).ShouldBeLessThan(64 * 1024);
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            root.GetProperty("schemaVersion").GetString().ShouldBe("rt.result.v2");
            root.GetProperty("outcome").GetString().ShouldBe("Partial");
            root.GetProperty("confidence").GetProperty("level").GetString().ShouldBe("Limited");
            var workspace = root.GetProperty("workspace");
            workspace.GetProperty("state").GetString().ShouldBe("Partial");
            var summary = workspace.GetProperty("evidenceSummary");
            summary.GetProperty("totalInputs").GetInt32().ShouldBe(5_102);
            summary.GetProperty("totalDiagnostics").GetInt32().ShouldBe(291);
            summary.GetProperty("inputsOmitted").GetInt32().ShouldBe(5_086);
            summary.GetProperty("diagnosticsOmitted").GetInt32().ShouldBe(267);
            summary.GetProperty("diagnosticGroupsOmitted").GetInt32().ShouldBe(18);
            var groups = summary.GetProperty("diagnosticGroups").EnumerateArray().ToArray();
            groups[0].GetProperty("code").GetString().ShouldBe("acquisition.evidence.1");
            groups[0].GetProperty("completenessImpact").GetString().ShouldBe("RequiredInputIncomplete");
            groups[1].GetProperty("code").GetString().ShouldBe("CS8019");
            groups[1].GetProperty("count").GetInt32().ShouldBe(250);
            workspace.GetProperty("diagnostics").GetArrayLength().ShouldBe(0);
            var outputDiagnostics = root.GetProperty("diagnostics").EnumerateArray().ToArray();
            outputDiagnostics.Length.ShouldBe(24);
            outputDiagnostics[0].GetProperty("code").GetString().ShouldBe("acquisition.evidence.1");
            outputDiagnostics[0].GetProperty("context").GetProperty("origin").GetString().ShouldBe("acquisition");
            outputDiagnostics[0].GetProperty("message").GetString()!.Length.ShouldBeLessThanOrEqualTo(512);
            var shownInputs = workspace.GetProperty("inputs").EnumerateArray().ToArray();
            shownInputs[0].GetProperty("id").GetString().ShouldBe("required-project:missing");
            shownInputs[0].GetProperty("detail").GetString()!.Length.ShouldBeLessThanOrEqualTo(512);
            shownInputs[1].GetProperty("id").GetString().ShouldBe("optional-project:excluded");
            shownInputs[2].GetProperty("id").GetString().ShouldBe("resolved-build-input:0");
            shownInputs.ShouldNotContain(item => item.GetProperty("state").GetString() == "Loaded");
        }
        finally
        {
            RtContext.Format = previousFormat;
            RtContext.FullWorkspaceEvidence = previousFullEvidence;
            Environment.ExitCode = previousExitCode;
        }
    }

    [Fact]
    public void incomplete_required_input_outweighs_error_groups_when_diagnostic_samples_are_full()
    {
        var previousFormat = RtContext.Format;
        var previousFullEvidence = RtContext.FullWorkspaceEvidence;
        using var console = new CapturingConsole();
        using var scope = RtConsole.UseConsole(console);
        try
        {
            RtContext.Format = OutputFormat.Json;
            RtContext.FullWorkspaceEvidence = false;

            RtResultRenderer.Write(RtResult<RtResultEnvelope<string>>.Ok(
                WithCompetingDiagnostics(LargePartialOutline())));

            using var document = JsonDocument.Parse(console.Text);
            var root = document.RootElement;
            root.GetProperty("schemaVersion").GetString().ShouldBe("rt.result.v2");
            var summary = root.GetProperty("workspace").GetProperty("evidenceSummary");
            summary.GetProperty("diagnosticGroupsOmitted").GetInt32().ShouldBe(7);
            var firstGroup = summary.GetProperty("diagnosticGroups")[0];
            firstGroup.GetProperty("code").GetString().ShouldBe("acquisition.evidence.1");
            firstGroup.GetProperty("completenessImpact").GetString()
                .ShouldBe("RequiredInputIncomplete");
            var firstDiagnostic = root.GetProperty("diagnostics")[0];
            firstDiagnostic.GetProperty("code").GetString().ShouldBe("acquisition.evidence.1");
            firstDiagnostic.GetProperty("context").GetProperty("completenessImpact")
                .GetString().ShouldBe("RequiredInputIncomplete");
        }
        finally
        {
            RtContext.Format = previousFormat;
            RtContext.FullWorkspaceEvidence = previousFullEvidence;
        }
    }

    [Fact]
    public void find_unused_keeps_completeness_warning_visible_when_other_warnings_are_hidden()
    {
        var previousFullEvidence = RtContext.FullWorkspaceEvidence;
        var previousShowWarnings = RtContext.ShowWorkspaceWarnings;
        try
        {
            RtContext.FullWorkspaceEvidence = false;
            RtContext.ShowWorkspaceWarnings = false;
            var envelope = WithCompetingDiagnostics(
                LargePartialEnvelope(new FindUnusedAnalysisResult([])));

            var result = new FindUnusedEnvelopeResult(envelope);

            result.Diagnostics.ShouldContain("Warning acquisition.evidence.1:");
        }
        finally
        {
            RtContext.FullWorkspaceEvidence = previousFullEvidence;
            RtContext.ShowWorkspaceWarnings = previousShowWarnings;
        }
    }

    [Fact]
    public void blocked_and_unknown_inputs_survive_sample_pressure()
    {
        var previousFormat = RtContext.Format;
        var previousFullEvidence = RtContext.FullWorkspaceEvidence;
        using var console = new CapturingConsole();
        using var scope = RtConsole.UseConsole(console);
        try
        {
            RtContext.Format = OutputFormat.Json;
            RtContext.FullWorkspaceEvidence = false;
            var envelope = LargePartialOutline();
            var inputs = envelope.Workspace.Inputs!
                .Concat([
                    new RtWorkspaceInput("required-project:blocked", "Project", "PolicyBlocked", true),
                    new RtWorkspaceInput("required-project:unknown", "Project", "Unknown", true)
                ])
                .ToArray();

            RtResultRenderer.Write(RtResult<RtResultEnvelope<string>>.Ok(
                envelope with { Workspace = envelope.Workspace with { Inputs = inputs } }));

            using var document = JsonDocument.Parse(console.Text);
            var workspace = document.RootElement.GetProperty("workspace");
            workspace.GetProperty("evidenceSummary").GetProperty("totalInputs")
                .GetInt32().ShouldBe(5_104);
            var shownInputs = workspace.GetProperty("inputs").EnumerateArray()
                .Select(item => item.GetProperty("id").GetString()).ToArray();
            shownInputs.ShouldContain("required-project:missing");
            shownInputs.ShouldContain("required-project:blocked");
            shownInputs.ShouldContain("required-project:unknown");
            shownInputs.Length.ShouldBe(16);
        }
        finally
        {
            RtContext.Format = previousFormat;
            RtContext.FullWorkspaceEvidence = previousFullEvidence;
        }
    }

    [Fact]
    public void explicit_full_workspace_evidence_restores_all_inputs_and_diagnostics()
    {
        var previousFormat = RtContext.Format;
        var previousFullEvidence = RtContext.FullWorkspaceEvidence;
        var previousExitCode = Environment.ExitCode;
        using var console = new CapturingConsole();
        using var scope = RtConsole.UseConsole(console);
        try
        {
            RtContext.Format = OutputFormat.Json;
            RtContext.FullWorkspaceEvidence = true;

            RtResultRenderer.Write(RtResult<RtResultEnvelope<string>>.Ok(LargePartialOutline()));

            using var document = JsonDocument.Parse(console.Text);
            var root = document.RootElement;
            root.GetProperty("schemaVersion").GetString().ShouldBe("rt.result.v1");
            root.GetProperty("workspace").GetProperty("inputs").GetArrayLength().ShouldBe(5_102);
            root.GetProperty("workspace").GetProperty("diagnostics").GetArrayLength().ShouldBe(291);
            root.GetProperty("diagnostics").GetArrayLength().ShouldBe(291);
            root.GetProperty("workspace").TryGetProperty("evidenceSummary", out _).ShouldBeFalse();
        }
        finally
        {
            RtContext.Format = previousFormat;
            RtContext.FullWorkspaceEvidence = previousFullEvidence;
            Environment.ExitCode = previousExitCode;
        }
    }

    [Fact]
    public void small_inspection_envelope_keeps_its_existing_schema()
    {
        var previousFormat = RtContext.Format;
        var previousFullEvidence = RtContext.FullWorkspaceEvidence;
        using var console = new CapturingConsole();
        using var scope = RtConsole.UseConsole(console);
        try
        {
            RtContext.Format = OutputFormat.Json;
            RtContext.FullWorkspaceEvidence = false;
            var large = LargePartialOutline();
            var small = large with
            {
                Outcome = "Complete",
                Workspace = large.Workspace with { State = "Complete", Inputs = [], Diagnostics = [] },
                Diagnostics = [],
                Limitations = []
            };

            RtResultRenderer.Write(RtResult<RtResultEnvelope<string>>.Ok(small));

            using var document = JsonDocument.Parse(console.Text);
            document.RootElement.GetProperty("schemaVersion").GetString().ShouldBe("rt.result.v1");
            document.RootElement.GetProperty("workspace").TryGetProperty("evidenceSummary", out _)
                .ShouldBeFalse();
        }
        finally
        {
            RtContext.Format = previousFormat;
            RtContext.FullWorkspaceEvidence = previousFullEvidence;
        }
    }

    [Theory]
    [InlineData(16, 24, 8, "rt.result.v1")]
    [InlineData(17, 24, 8, "rt.result.v2")]
    [InlineData(16, 25, 8, "rt.result.v2")]
    [InlineData(16, 24, 9, "rt.result.v2")]
    public void evidence_limits_select_the_expected_schema(
        int inputCount, int diagnosticCount, int legacyCount, string expectedSchema)
    {
        var previousFormat = RtContext.Format;
        var previousFullEvidence = RtContext.FullWorkspaceEvidence;
        using var console = new CapturingConsole();
        using var scope = RtConsole.UseConsole(console);
        try
        {
            RtContext.Format = OutputFormat.Json;
            RtContext.FullWorkspaceEvidence = false;
            var large = LargePartialOutline();
            var boundary = large with
            {
                Workspace = large.Workspace with
                {
                    Inputs = large.Workspace.Inputs!.Take(inputCount).ToArray(),
                    Diagnostics = large.Workspace.Diagnostics.Take(legacyCount).ToArray()
                },
                Diagnostics = large.Diagnostics.Take(diagnosticCount).ToArray()
            };

            RtResultRenderer.Write(RtResult<RtResultEnvelope<string>>.Ok(boundary));

            using var document = JsonDocument.Parse(console.Text);
            document.RootElement.GetProperty("schemaVersion").GetString().ShouldBe(expectedSchema);
        }
        finally
        {
            RtContext.Format = previousFormat;
            RtContext.FullWorkspaceEvidence = previousFullEvidence;
        }
    }

    [Theory]
    [InlineData(false, "rt.result.v2")]
    [InlineData(true, "rt.result.v1")]
    public void find_unused_json_honors_workspace_evidence_selection(bool full, string expectedVersion)
    {
        var previousFormat = RtContext.Format;
        var previousFullEvidence = RtContext.FullWorkspaceEvidence;
        using var console = new CapturingConsole();
        using var scope = RtConsole.UseConsole(console);
        try
        {
            RtContext.Format = OutputFormat.Json;
            RtContext.FullWorkspaceEvidence = full;

            var result = new FindUnusedEnvelopeResult(
                LargePartialEnvelope(new FindUnusedAnalysisResult([])));
            RtResultRenderer.Write(RtResult<FindUnusedEnvelopeResult>.Ok(result));

            using var document = JsonDocument.Parse(console.Text);
            document.RootElement.GetProperty("schemaVersion").GetString().ShouldBe(expectedVersion);
            var workspace = document.RootElement.GetProperty("workspace");
            if (full)
                workspace.GetProperty("inputs").GetArrayLength().ShouldBe(5_102);
            else
                workspace.GetProperty("evidenceSummary")
                    .GetProperty("totalInputs").GetInt32().ShouldBe(5_102);
        }
        finally
        {
            RtContext.Format = previousFormat;
            RtContext.FullWorkspaceEvidence = previousFullEvidence;
        }
    }

    [Fact]
    public void legacy_only_workspace_diagnostics_keep_their_exact_count()
    {
        var previousFormat = RtContext.Format;
        var previousFullEvidence = RtContext.FullWorkspaceEvidence;
        using var console = new CapturingConsole();
        using var scope = RtConsole.UseConsole(console);
        try
        {
            RtContext.Format = OutputFormat.Json;
            RtContext.FullWorkspaceEvidence = false;

            RtResultRenderer.Write(RtResult<RtResultEnvelope<string>>.Ok(
                LargePartialOutline() with { Diagnostics = [] }));

            using var document = JsonDocument.Parse(console.Text);
            var workspace = document.RootElement.GetProperty("workspace");
            workspace.GetProperty("evidenceSummary").GetProperty("totalDiagnostics")
                .GetInt32().ShouldBe(291);
            workspace.GetProperty("evidenceSummary").GetProperty("diagnosticsOmitted")
                .GetInt32().ShouldBe(283);
            workspace.GetProperty("diagnostics").GetArrayLength().ShouldBe(8);
        }
        finally
        {
            RtContext.Format = previousFormat;
            RtContext.FullWorkspaceEvidence = previousFullEvidence;
        }
    }

    private static RtResultEnvelope<string> LargePartialOutline() => LargePartialEnvelope("Order");

    private static RtResultEnvelope<T> WithCompetingDiagnostics<T>(RtResultEnvelope<T> envelope)
    {
        var diagnostics = Enumerable.Range(0, 30)
            .Select(index => new RtStructuredDiagnostic(
                $"ERROR{index:D2}",
                "Error",
                $"Compiler error {index}.",
                new Dictionary<string, string>
                {
                    ["origin"] = "compiler",
                    ["completenessImpact"] = "None"
                }))
            .Append(envelope.Diagnostics[^1])
            .ToArray();
        return envelope with
        {
            Diagnostics = diagnostics,
            Workspace = envelope.Workspace with
            {
                Diagnostics = diagnostics.Select(diagnostic => diagnostic.Message).ToArray()
            }
        };
    }

    private static RtResultEnvelope<T> LargePartialEnvelope<T>(T data)
    {
        var inputs = Enumerable.Range(0, 5_000)
            .Select(index => new RtWorkspaceInput(
                $"source-document:{index}",
                "source-document",
                "Loaded",
                true,
                "/repo/Project.csproj",
                $"/repo/src/Document{index}.cs"))
            .Concat(Enumerable.Range(0, 100).Select(index => new RtWorkspaceInput(
                $"resolved-build-input:{index}",
                "BuildInput",
                "Resolved",
                true,
                "/repo/Project.csproj")))
            .Append(new RtWorkspaceInput(
                "optional-project:excluded",
                "Project",
                "Excluded",
                false,
                "/repo/Optional.csproj"))
            .Append(new RtWorkspaceInput(
                "required-project:missing",
                "Project",
                "Failed",
                true,
                "/repo/Missing.csproj",
                new string('x', 2_000)))
            .ToArray();
        var diagnostics = Enumerable.Range(0, 250)
            .Select(index => new RtStructuredDiagnostic(
                "CS8019",
                "Hidden",
                "Unnecessary using directive.",
                new Dictionary<string, string>
                {
                    ["origin"] = "compiler",
                    ["completenessImpact"] = "None",
                    ["inputId"] = $"compiler:/repo/src/Document{index}.cs"
                }))
            .Concat(Enumerable.Range(0, 40).Select(index => new RtStructuredDiagnostic(
                $"INFO{index:D2}",
                "Info",
                $"Optional analyzer observation {index}.",
                new Dictionary<string, string>
                {
                    ["origin"] = "compiler",
                    ["completenessImpact"] = "None"
                })))
            .Append(new RtStructuredDiagnostic(
                "acquisition.evidence.1",
                "Warning",
                "A required semantic input has no unique acquisition outcome. " + new string('x', 2_000),
                new Dictionary<string, string>
                {
                    ["origin"] = "acquisition",
                    ["completenessImpact"] = "RequiredInputIncomplete"
                }))
            .ToArray();
        return new RtResultEnvelope<T>(
            "rt.result.v1",
            "outline",
            "Partial",
            new RtSourceIdentity("/repo", "/repo/Project.slnx", "revision"),
            new RtWorkspaceProvenance(
                "Partial",
                1,
                diagnostics.Select(diagnostic => diagnostic.Message).ToArray(),
                inputs,
                ["/repo/Project.csproj"],
                [],
                "test-compiler",
                "test-policy"),
            data,
            new RtConfidence("Limited", "Roslyn semantic workspace load", [], ["The workspace is Partial."]),
            diagnostics,
            ["The workspace is Partial."],
            [],
            new RtResultSafety(["RepositoryRead"], false, false),
            new RtTiming(1),
            new RtPaging(1, 1, false, null));
    }

    private sealed class CapturingConsole : IRtConsole, IDisposable
    {
        private readonly StringBuilder _text = new();
        private readonly StringWriter _writer;

        public CapturingConsole() => _writer = new StringWriter(_text);

        public bool IsOutputRedirected => true;
        public string Text => _text.ToString();
        public TextWriter Out => _writer;

        public void Dispose() => _writer.Dispose();

        public void Log(RtLogLevel level, string template, params object?[] args) { }

        public void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null)
            => _text.AppendLine(message);

        public void Write(RtOutputContext context, string message, ConsoleColor? color = null)
            => _text.Append(message);
    }
}
