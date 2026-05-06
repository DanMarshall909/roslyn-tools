using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Models;
using RoslynTools.Services;

namespace RoslynTools.Tests.Analysis;

public sealed class AnalyzeInterfaceTests : IDisposable
{
    public AnalyzeInterfaceTests()
    {
        RtContext.Format = OutputFormat.Text;
    }

    public void Dispose()
    {
        RtContext.Format = OutputFormat.Text;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Groups_consumers_by_the_exact_interface_members_they_use()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await AnalyzeInterfaceService.AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions());

            var runAndNameConsumer = FindConsumer(report, "Demo.App.RunAndNameConsumer");
            Assert.Equal("members", runAndNameConsumer.UsageKind);
            Assert.Equal(
                [
                    "string Demo.Contracts.IWorker.Name { get; }",
                    "void Demo.Contracts.IWorker.Run()",
                ],
                runAndNameConsumer.UsedMembers.Select(m => m.DisplayName).ToArray());

            var stopAndEventConsumer = FindConsumer(report, "Demo.App.StopAndEventConsumer");
            Assert.Equal(
                [
                    "event System.EventHandler? Demo.Contracts.IWorker.Changed",
                    "void Demo.Contracts.IWorker.Stop()",
                ],
                stopAndEventConsumer.UsedMembers.Select(m => m.DisplayName).ToArray());

            var localVariableConsumer = FindConsumer(report, "Demo.App.LocalVariableConsumer");
            Assert.Equal(
                ["void Demo.Contracts.IWorker.Stop()"],
                localVariableConsumer.UsedMembers.Select(m => m.DisplayName).ToArray());
        }
    }

    [Fact]
    public async Task Reports_consumers_with_no_direct_member_calls_explicitly()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await AnalyzeInterfaceService.AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions());

            var consumer = FindConsumer(report, "Demo.App.ZeroMemberConsumer");
            Assert.Equal("type-only", consumer.UsageKind);
            Assert.Empty(consumer.UsedMembers);
        }
    }

    [Fact]
    public async Task Reports_type_only_interface_references_for_properties_and_locals()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await AnalyzeInterfaceService.AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions());

            var propertyConsumer = FindConsumer(report, "Demo.App.TypeOnlyPropertyConsumer");
            Assert.Equal("type-only", propertyConsumer.UsageKind);
            Assert.Empty(propertyConsumer.UsedMembers);

            var localConsumer = FindConsumer(report, "Demo.App.LocalTypeOnlyConsumer");
            Assert.Equal("type-only", localConsumer.UsageKind);
            Assert.Empty(localConsumer.UsedMembers);
        }
    }

    [Fact]
    public async Task Combines_cross_file_usage_under_a_single_consumer()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await AnalyzeInterfaceService.AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions());

            var consumer = FindConsumer(report, "Demo.App.CrossFileConsumer");
            Assert.Equal("members", consumer.UsageKind);
            Assert.Equal(["CrossFileConsumer.Fields.cs", "CrossFileConsumer.Use.cs"], consumer.Documents);
            Assert.Equal(
                ["void Demo.Contracts.IWorker.Run()"],
                consumer.UsedMembers.Select(m => m.DisplayName).ToArray());
        }
    }

    [Fact]
    public async Task Excludes_nameof_references_and_unrelated_same_name_members()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await AnalyzeInterfaceService.AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions());

            Assert.DoesNotContain(report.Consumers, consumer => consumer.ConsumerDisplayName == "Demo.App.NameofOnlyConsumer");
            Assert.DoesNotContain(report.Consumers, consumer => consumer.ConsumerDisplayName == "Demo.App.SameNameConcreteConsumer");
        }
    }

    [Fact]
    public async Task Scope_filter_limits_consumers_by_namespace_prefix()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await AnalyzeInterfaceService.AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions(Scope: "Demo.App"));

            Assert.DoesNotContain(report.Consumers, consumer => consumer.ConsumerDisplayName == "Demo.Other.ScopedOutConsumer");
            Assert.Contains(report.Consumers, consumer => consumer.ConsumerDisplayName == "Demo.App.RunAndNameConsumer");
        }
    }

    [Fact]
    public async Task Json_output_contains_interface_consumer_and_used_member_fields()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await AnalyzeInterfaceService.AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions());

            var result = AnalyzeInterfaceResult.FromReport(report);
            using var output = new StringWriter();
            RtContext.Format = OutputFormat.Json;
            RtSerializer.Write(result, output);

            using var json = JsonDocument.Parse(output.ToString());
            var root = json.RootElement;

            Assert.Equal("Demo.Contracts.IWorker", root.GetProperty("interface").GetProperty("displayName").GetString());
            Assert.True(root.GetProperty("consumerCount").GetInt32() >= 1);

            var consumers = root.GetProperty("consumers").EnumerateArray().ToList();
            var runAndName = consumers.Single(c =>
                c.GetProperty("consumer").GetProperty("displayName").GetString() == "Demo.App.RunAndNameConsumer");

            Assert.Equal("members", runAndName.GetProperty("usageKind").GetString());
            Assert.Equal(
                ["string Demo.Contracts.IWorker.Name { get; }", "void Demo.Contracts.IWorker.Run()"],
                runAndName.GetProperty("usedMembers")
                    .EnumerateArray()
                    .Select(member => member.GetProperty("displayName").GetString()!)
                    .ToArray());
        }
    }

    [Fact]
    public async Task Non_interface_symbols_produce_a_clear_error_and_non_zero_exit()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var (exitCode, _, stdErr) = await InvokeAsync([
                "analyze-interface",
                "Demo.Contracts.ConcreteWorker",
                "--solution",
                "ignored",
            ]);

            Assert.NotEqual(0, exitCode);
            Assert.Contains("is not an interface", stdErr, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Registers_the_command_and_exposes_the_expected_options()
    {
        Assert.Contains(App.Commands, entry => entry.Info.Name == "analyze-interface");

        var command = AnalyzeInterfaceCommand.Build();

        Assert.Contains(command.Arguments, argument => argument.Name == "interface");
        Assert.Contains(command.Options, option => option.Name == "solution");
        Assert.Contains(command.Options, option => option.Name == "scope");
        Assert.Contains(command.Options, option => option.Name == "include-tests");

        var formats = AnalyzeInterfaceCommand.Info.SupportedFormats;
        Assert.NotNull(formats);
        Assert.Contains(OutputFormat.Text, formats);
        Assert.Contains(OutputFormat.Json, formats);
        Assert.Contains(OutputFormat.Markdown, formats);
        Assert.Contains(OutputFormat.Llm, formats);
    }

    // --suggest tests (AC 1–8)

    [Fact]
    public async Task Suggest_flag_is_registered_on_the_command()
    {
        var command = AnalyzeInterfaceCommand.Build();
        Assert.Contains(command.Options, option => option.Name == "suggest");
    }

    [Fact]
    public async Task Suggest_produces_strict_subset_for_partial_consumer()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await AnalyzeInterfaceService.AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions(Suggest: true));

            // RunAndNameConsumer uses {Name, Run} out of 4 — strict subset
            var consumer = FindConsumer(report, "Demo.App.RunAndNameConsumer");
            Assert.NotNull(consumer.Suggestion);
            Assert.True(consumer.Suggestion!.HasNarrowingBenefit);
            Assert.Equal(
                ["string Demo.Contracts.IWorker.Name { get; }", "void Demo.Contracts.IWorker.Run()"],
                consumer.Suggestion.SuggestedMembers.Select(m => m.DisplayName).ToArray());
        }
    }

    [Fact]
    public async Task Suggest_suggested_members_do_not_include_unused_members()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await AnalyzeInterfaceService.AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions(Suggest: true));

            var consumer = FindConsumer(report, "Demo.App.RunAndNameConsumer");
            Assert.NotNull(consumer.Suggestion);
            var suggestedNames = consumer.Suggestion!.SuggestedMembers.Select(m => m.Name).ToHashSet();
            Assert.DoesNotContain("Stop", suggestedNames);
            Assert.DoesNotContain("Changed", suggestedNames);
        }
    }

    [Fact]
    public async Task Suggest_consumers_sharing_same_subset_have_matching_group_id()
    {
        using var project = BuildSuggestProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await AnalyzeInterfaceService.AnalyzeAsync(
                sln,
                "Suggest.Contracts.IService",
                new AnalyzeInterfaceOptions(Suggest: true));

            var a = FindConsumer(report, "Suggest.App.ConsumerA");
            var b = FindConsumer(report, "Suggest.App.ConsumerB");

            Assert.NotNull(a.Suggestion);
            Assert.NotNull(b.Suggestion);
            Assert.Equal(a.Suggestion!.SharedGroupId, b.Suggestion!.SharedGroupId);
        }
    }

    [Fact]
    public async Task Suggest_consumer_using_full_interface_reports_no_narrowing_benefit()
    {
        using var project = BuildSuggestProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await AnalyzeInterfaceService.AnalyzeAsync(
                sln,
                "Suggest.Contracts.IService",
                new AnalyzeInterfaceOptions(Suggest: true));

            var full = FindConsumer(report, "Suggest.App.FullConsumer");
            Assert.NotNull(full.Suggestion);
            Assert.False(full.Suggestion!.HasNarrowingBenefit);
        }
    }

    [Fact]
    public async Task Suggest_without_flag_leaves_suggestion_null()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await AnalyzeInterfaceService.AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions(Suggest: false));

            var consumer = FindConsumer(report, "Demo.App.RunAndNameConsumer");
            Assert.Null(consumer.Suggestion);
        }
    }

    [Fact]
    public async Task Suggest_json_output_includes_suggestion_payload()
    {
        using var project = BuildSuggestProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await AnalyzeInterfaceService.AnalyzeAsync(
                sln,
                "Suggest.Contracts.IService",
                new AnalyzeInterfaceOptions(Suggest: true));

            var result = AnalyzeInterfaceResult.FromReport(report, suggest: true);
            using var output = new StringWriter();
            RtContext.Format = OutputFormat.Json;
            RtSerializer.Write(result, output);

            using var json = JsonDocument.Parse(output.ToString());
            var consumers = json.RootElement.GetProperty("consumers").EnumerateArray().ToList();

            var fullEntry = consumers.Single(c =>
                c.GetProperty("consumer").GetProperty("displayName").GetString() == "Suggest.App.FullConsumer");

            var suggestion = fullEntry.GetProperty("suggestion");
            Assert.False(suggestion.GetProperty("hasNarrowingBenefit").GetBoolean());

            var subsetEntry = consumers.Single(c =>
                c.GetProperty("consumer").GetProperty("displayName").GetString() == "Suggest.App.ConsumerA");

            var subsetSuggestion = subsetEntry.GetProperty("suggestion");
            Assert.True(subsetSuggestion.GetProperty("hasNarrowingBenefit").GetBoolean());
            Assert.True(subsetSuggestion.GetProperty("suggestedMembers").GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task Suggest_text_output_is_readable_and_labeled_advisory()
    {
        using var project = BuildSuggestProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await AnalyzeInterfaceService.AnalyzeAsync(
                sln,
                "Suggest.Contracts.IService",
                new AnalyzeInterfaceOptions(Suggest: true));

            var result = AnalyzeInterfaceResult.FromReport(report, suggest: true);
            using var output = new StringWriter();
            result.RenderText(output);
            var text = output.ToString();

            // advisory label present
            Assert.Contains("[advisory]", text, StringComparison.OrdinalIgnoreCase);
            // no narrowing benefit explicit message
            Assert.Contains("no narrowing benefit", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static AdhocProjectBuilder BuildSuggestProject()
    {
        var project = new AdhocProjectBuilder("SuggestApp");
        project.AddSource("Contracts.cs", """
            namespace Suggest.Contracts;

            public interface IService
            {
                void Alpha();
                void Beta();
                void Gamma();
            }
            """);

        project.AddSource("Consumers.cs", """
            namespace Suggest.App;

            using Suggest.Contracts;

            // Uses only Alpha — strict subset, same as ConsumerB
            public sealed class ConsumerA
            {
                public void Use(IService svc) => svc.Alpha();
            }

            // Uses only Alpha — strict subset, same as ConsumerA
            public sealed class ConsumerB
            {
                public void Use(IService svc) => svc.Alpha();
            }

            // Uses all three members — no narrowing benefit
            public sealed class FullConsumer
            {
                public void Use(IService svc)
                {
                    svc.Alpha();
                    svc.Beta();
                    svc.Gamma();
                }
            }
            """);

        return project;
    }

    private static AdhocProjectBuilder BuildInterfaceProject()
    {
        var project = new AdhocProjectBuilder("DemoApp");
        project.AddSource("Contracts.cs", """
            namespace Demo.Contracts;

            public interface IWorker
            {
                string Name { get; }
                event System.EventHandler? Changed;
                void Run();
                void Stop();
            }

            public interface IWorkerFactory
            {
                IWorker Create();
            }

            public sealed class ConcreteWorker
            {
                public string Name => "concrete";
                public void Run() { }
                public void Stop() { }
            }
            """);

        project.AddSource("Consumers.cs", """
            namespace Demo.App;

            using Demo.Contracts;

            public sealed class RunAndNameConsumer
            {
                private readonly IWorker _worker;

                public RunAndNameConsumer(IWorker worker)
                {
                    _worker = worker;
                }

                public string Use()
                {
                    _worker.Run();
                    return _worker.Name;
                }
            }

            public sealed class StopAndEventConsumer
            {
                public void Use(IWorker worker)
                {
                    worker.Stop();
                    worker.Changed += HandleChanged;
                }

                private static void HandleChanged(object? sender, System.EventArgs args)
                {
                }
            }

            public sealed class LocalVariableConsumer
            {
                private readonly IWorkerFactory _factory;

                public LocalVariableConsumer(IWorkerFactory factory)
                {
                    _factory = factory;
                }

                public void Use()
                {
                    var worker = _factory.Create();
                    worker.Stop();
                }
            }

            public sealed class ZeroMemberConsumer
            {
                private readonly IWorker _worker;

                public ZeroMemberConsumer(IWorker worker)
                {
                    _worker = worker;
                }
            }

            public sealed class TypeOnlyPropertyConsumer
            {
                public TypeOnlyPropertyConsumer(IWorker worker)
                {
                    Worker = worker;
                }

                public IWorker Worker { get; }
            }

            public sealed class LocalTypeOnlyConsumer
            {
                public void Use(IWorkerFactory factory)
                {
                    IWorker worker = factory.Create();
                }
            }

            public sealed class NameofOnlyConsumer
            {
                public string GetName() => nameof(IWorker.Run) + nameof(IWorker.Name);
            }

            public sealed class SameNameConcreteConsumer
            {
                private readonly ConcreteWorker _worker;

                public SameNameConcreteConsumer(ConcreteWorker worker)
                {
                    _worker = worker;
                }

                public string Use()
                {
                    _worker.Run();
                    return _worker.Name;
                }
            }
            """);

        project.AddSource("CrossFileConsumer.Fields.cs", """
            namespace Demo.App;

            using Demo.Contracts;

            public sealed partial class CrossFileConsumer
            {
                private readonly IWorker _worker;

                public CrossFileConsumer(IWorker worker)
                {
                    _worker = worker;
                }
            }
            """);

        project.AddSource("CrossFileConsumer.Use.cs", """
            namespace Demo.App;

            public sealed partial class CrossFileConsumer
            {
                public void Use()
                {
                    _worker.Run();
                }
            }
            """);

        project.AddSource("ScopedOutConsumer.cs", """
            namespace Demo.Other;

            using Demo.Contracts;

            public sealed class ScopedOutConsumer
            {
                public void Use(IWorker worker)
                {
                    worker.Run();
                }
            }
            """);

        return project;
    }

    private static AnalyzeInterfaceConsumerReport FindConsumer(
        AnalyzeInterfaceReport report,
        string consumerDisplayName) =>
        report.Consumers.Single(consumer =>
            consumer.ConsumerDisplayName == consumerDisplayName);

    private static async Task<(int ExitCode, string StdOut, string StdErr)> InvokeAsync(string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        Console.SetOut(stdout);
        Console.SetError(stderr);

        try
        {
            var exitCode = await App.RunAsync(args);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
