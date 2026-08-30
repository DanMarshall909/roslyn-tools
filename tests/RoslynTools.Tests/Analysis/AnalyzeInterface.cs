using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Commands;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Models;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public sealed class AnalyzeInterface : IClassFixture<RtServicesFixture>, IDisposable
{
    private readonly RtServicesFixture _services;
    public AnalyzeInterface(RtServicesFixture services)
    {
        _services = services;
        RtContext.Format = OutputFormat.Text;
    }

    public void Dispose()
    {
        RtContext.Format = OutputFormat.Text;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task groups_consumers_by_the_exact_interface_members_they_use()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await new AnalyzeInterfaceService().AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions());

            var runAndNameConsumer = FindConsumer(report, "Demo.App.RunAndNameConsumer");
            runAndNameConsumer.UsageKind.ShouldBe("members");
            runAndNameConsumer.UsedMembers.Select(m => m.DisplayName).ToArray().ShouldBe([
                    "string Demo.Contracts.IWorker.Name { get; }",
                    "void Demo.Contracts.IWorker.Run()"
                ]);

            var stopAndEventConsumer = FindConsumer(report, "Demo.App.StopAndEventConsumer");
            stopAndEventConsumer.UsedMembers.Select(m => m.DisplayName).ToArray().ShouldBe([
                    "event System.EventHandler? Demo.Contracts.IWorker.Changed",
                    "void Demo.Contracts.IWorker.Stop()"
                ]);

            var localVariableConsumer = FindConsumer(report, "Demo.App.LocalVariableConsumer");
            localVariableConsumer.UsedMembers.Select(m => m.DisplayName).ToArray().ShouldBe(["void Demo.Contracts.IWorker.Stop()"]);
        }
    }

    [Fact]
    public async Task reports_consumers_with_no_direct_member_calls_explicitly()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await new AnalyzeInterfaceService().AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions());

            var consumer = FindConsumer(report, "Demo.App.ZeroMemberConsumer");
            consumer.UsageKind.ShouldBe("type-only");
            consumer.UsedMembers.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task reports_type_only_interface_references_for_properties_and_locals()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await new AnalyzeInterfaceService().AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions());

            var propertyConsumer = FindConsumer(report, "Demo.App.TypeOnlyPropertyConsumer");
            propertyConsumer.UsageKind.ShouldBe("type-only");
            propertyConsumer.UsedMembers.ShouldBeEmpty();

            var localConsumer = FindConsumer(report, "Demo.App.LocalTypeOnlyConsumer");
            localConsumer.UsageKind.ShouldBe("type-only");
            localConsumer.UsedMembers.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task combines_cross_file_usage_under_a_single_consumer()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await new AnalyzeInterfaceService().AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions());

            var consumer = FindConsumer(report, "Demo.App.CrossFileConsumer");
            consumer.UsageKind.ShouldBe("members");
            consumer.Documents.ShouldBe(["CrossFileConsumer.Fields.cs", "CrossFileConsumer.Use.cs"]);
            consumer.UsedMembers.Select(m => m.DisplayName).ToArray().ShouldBe(["void Demo.Contracts.IWorker.Run()"]);
        }
    }

    [Fact]
    public async Task excludes_nameof_references_and_unrelated_same_name_members()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await new AnalyzeInterfaceService().AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions());

            report.Consumers.ShouldNotContain(consumer => consumer.ConsumerDisplayName == "Demo.App.NameofOnlyConsumer");
            report.Consumers.ShouldNotContain(consumer => consumer.ConsumerDisplayName == "Demo.App.SameNameConcreteConsumer");
        }
    }

    [Fact]
    public async Task limits_consumers_by_namespace_prefix_when_scope_filter_is_used()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await new AnalyzeInterfaceService().AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions("Demo.App"));

            report.Consumers.ShouldNotContain(consumer => consumer.ConsumerDisplayName == "Demo.Other.ScopedOutConsumer");
            report.Consumers.ShouldContain(consumer => consumer.ConsumerDisplayName == "Demo.App.RunAndNameConsumer");
        }
    }

    [Fact]
    public async Task emits_json_output_with_interface_consumer_and_used_member_fields()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await new AnalyzeInterfaceService().AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions());

            var result = AnalyzeInterfaceResult.FromReport(report);
            using var output = new StringWriter();
            RtContext.Format = OutputFormat.Json;
            RtSerializer.Write(result, output);

            using var json = JsonDocument.Parse(output.ToString());
            var root = json.RootElement;

            root.GetProperty("interface").GetProperty("displayName").GetString().ShouldBe("Demo.Contracts.IWorker");
            (root.GetProperty("consumerCount").GetInt32() >= 1).ShouldBeTrue();

            var consumers = root.GetProperty("consumers").EnumerateArray().ToList();
            var runAndName = consumers.Single(c =>
                c.GetProperty("consumer").GetProperty("displayName").GetString() == "Demo.App.RunAndNameConsumer");

            runAndName.GetProperty("usageKind").GetString().ShouldBe("members");
            runAndName.GetProperty("usedMembers")
                    .EnumerateArray()
                    .Select(member => member.GetProperty("displayName").GetString()!)
                    .ToArray().ShouldBe(["string Demo.Contracts.IWorker.Name { get; }", "void Demo.Contracts.IWorker.Run()"]);
        }
    }

    [Fact]
    public async Task rejects_non_interface_symbols_with_clear_error_and_non_zero_exit()
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
                "ignored"
            ]);

            exitCode.ShouldNotBe(0);
            Assert.Contains("is not an interface", stdErr, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void registers_the_command_and_exposes_the_expected_options()
    {
        App.Commands.ShouldContain(entry => entry.Info.Name == "analyze-interface");

        var command = _services.Resolve<AnalyzeInterfaceCommand>().Build();

        command.Arguments.ShouldContain(argument => argument.Name == "interface");
        command.Options.ShouldContain(option => option.Name == "solution");
        command.Options.ShouldContain(option => option.Name == "scope");
        command.Options.ShouldContain(option => option.Name == "include-tests");

        var formats = AnalyzeInterfaceCommand.Info.SupportedFormats;
        formats.ShouldNotBeNull();
        formats.ShouldContain(OutputFormat.Text);
        formats.ShouldContain(OutputFormat.Json);
        formats.ShouldContain(OutputFormat.Markdown);
        formats.ShouldContain(OutputFormat.Llm);
    }

    // --suggest tests (AC 1–8)

    [Fact]
    public async Task registers_suggest_flag_on_the_command()
    {
        var command = _services.Resolve<AnalyzeInterfaceCommand>().Build();
        command.Options.ShouldContain(option => option.Name == "suggest");
    }

    [Fact]
    public async Task suggests_strict_subset_for_partial_consumer()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await new AnalyzeInterfaceService().AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions(Suggest: true));

            // RunAndNameConsumer uses {Name, Run} out of 4 — strict subset
            var consumer = FindConsumer(report, "Demo.App.RunAndNameConsumer");
            consumer.Suggestion.ShouldNotBeNull();
            consumer.Suggestion!.HasNarrowingBenefit.ShouldBeTrue();
            consumer.Suggestion.SuggestedMembers.Select(m => m.DisplayName).ToArray().ShouldBe(["string Demo.Contracts.IWorker.Name { get; }", "void Demo.Contracts.IWorker.Run()"]);
        }
    }

    [Fact]
    public async Task excludes_unused_members_from_suggested_members()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await new AnalyzeInterfaceService().AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions(Suggest: true));

            var consumer = FindConsumer(report, "Demo.App.RunAndNameConsumer");
            consumer.Suggestion.ShouldNotBeNull();
            var suggestedNames = consumer.Suggestion!.SuggestedMembers.Select(m => m.Name).ToHashSet();
            suggestedNames.ShouldNotContain("Stop");
            suggestedNames.ShouldNotContain("Changed");
        }
    }

    [Fact]
    public async Task assigns_matching_group_id_to_consumers_sharing_same_subset()
    {
        using var project = BuildSuggestProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await new AnalyzeInterfaceService().AnalyzeAsync(
                sln,
                "Suggest.Contracts.IService",
                new AnalyzeInterfaceOptions(Suggest: true));

            var a = FindConsumer(report, "Suggest.App.ConsumerA");
            var b = FindConsumer(report, "Suggest.App.ConsumerB");

            a.Suggestion.ShouldNotBeNull();
            b.Suggestion.ShouldNotBeNull();
            b.Suggestion!.SharedGroupId.ShouldBe(a.Suggestion!.SharedGroupId);
        }
    }

    [Fact]
    public async Task reports_no_narrowing_benefit_for_full_interface_consumer()
    {
        using var project = BuildSuggestProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await new AnalyzeInterfaceService().AnalyzeAsync(
                sln,
                "Suggest.Contracts.IService",
                new AnalyzeInterfaceOptions(Suggest: true));

            var full = FindConsumer(report, "Suggest.App.FullConsumer");
            full.Suggestion.ShouldNotBeNull();
            full.Suggestion!.HasNarrowingBenefit.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task leaves_suggestion_null_without_suggest_flag()
    {
        using var project = BuildInterfaceProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await new AnalyzeInterfaceService().AnalyzeAsync(
                sln,
                "Demo.Contracts.IWorker",
                new AnalyzeInterfaceOptions(Suggest: false));

            var consumer = FindConsumer(report, "Demo.App.RunAndNameConsumer");
            consumer.Suggestion.ShouldBeNull();
        }
    }

    [Fact]
    public async Task includes_suggestion_payload_in_suggest_json_output()
    {
        using var project = BuildSuggestProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await new AnalyzeInterfaceService().AnalyzeAsync(
                sln,
                "Suggest.Contracts.IService",
                new AnalyzeInterfaceOptions(Suggest: true));

            var result = AnalyzeInterfaceResult.FromReport(report, true);
            using var output = new StringWriter();
            RtContext.Format = OutputFormat.Json;
            RtSerializer.Write(result, output);

            using var json = JsonDocument.Parse(output.ToString());
            var consumers = json.RootElement.GetProperty("consumers").EnumerateArray().ToList();

            var fullEntry = consumers.Single(c =>
                c.GetProperty("consumer").GetProperty("displayName").GetString() == "Suggest.App.FullConsumer");

            var suggestion = fullEntry.GetProperty("suggestion");
            suggestion.GetProperty("hasNarrowingBenefit").GetBoolean().ShouldBeFalse();

            var subsetEntry = consumers.Single(c =>
                c.GetProperty("consumer").GetProperty("displayName").GetString() == "Suggest.App.ConsumerA");

            var subsetSuggestion = subsetEntry.GetProperty("suggestion");
            subsetSuggestion.GetProperty("hasNarrowingBenefit").GetBoolean().ShouldBeTrue();
            (subsetSuggestion.GetProperty("suggestedMembers").GetArrayLength() > 0).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task emits_readable_advisory_suggest_text_output()
    {
        using var project = BuildSuggestProject();
        var (workspace, sln) = project.Build();

        using (workspace)
        {
            var report = await new AnalyzeInterfaceService().AnalyzeAsync(
                sln,
                "Suggest.Contracts.IService",
                new AnalyzeInterfaceOptions(Suggest: true));

            var result = AnalyzeInterfaceResult.FromReport(report, true);
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
        string consumerDisplayName)
    {
        return report.Consumers.Single(consumer =>
            consumer.ConsumerDisplayName == consumerDisplayName);
    }

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
            var services = new ServiceCollection();
            global::RoslynTools.Bootstrap.RegisterServices(services);
            services.AddSingleton<IDaemonRpcClient, UnavailableDaemonRpcClient>();
            services.AddSingleton<IDaemonLauncher, UnavailableDaemonLauncher>();
            await using var provider = services.BuildServiceProvider();
            var exitCode = await App.RunAsync(args, provider);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private sealed class UnavailableDaemonRpcClient : IDaemonRpcClient
    {
        public Task<DaemonRpcAttempt> ExecuteAsync(
            string commandName,
            JsonElement arguments,
            CommandSafety safety,
            CancellationToken cancellationToken = default) => Task.FromResult(new DaemonRpcAttempt(
            DaemonRpcAttemptState.UnavailableBeforeDispatch,
            null,
            DispatchStarted: false));
    }

    private sealed class UnavailableDaemonLauncher : IDaemonLauncher
    {
        public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
