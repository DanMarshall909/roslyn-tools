using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class NarrowDependency : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public NarrowDependency(RtServicesFixture services) => _services = services;

    // -------------------------------------------------------------------------
    // AC1 + AC3: extracts selected members into a new interface file
    // -------------------------------------------------------------------------
    [Fact]
    public async Task extracts_new_interface_with_selected_members()
    {
        using var project = BuildWideFacadeProject();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<NarrowDependencyCommand>().ExecuteAsync(
                "IWideFacade",
                ["Send"],
                "ISender",
                "ignored",
                false);
        }

        Assert.True(project.SourceExists("ISender.cs"),
            "ISender.cs should have been created");

        var content = await project.ReadSourceAsync("ISender.cs");
        content.ShouldContain("ISender", Case.Sensitive);
        content.ShouldContain("Send", Case.Sensitive);
        content.ShouldNotContain("Receive", Case.Sensitive);
        content.ShouldNotContain("Delete", Case.Sensitive);
    }

    // -------------------------------------------------------------------------
    // AC5: narrows a consumer whose usage matches the selected subset
    // -------------------------------------------------------------------------
    [Fact]
    public async Task narrows_consumer_that_uses_only_selected_members()
    {
        using var project = BuildWideFacadeProject();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<NarrowDependencyCommand>().ExecuteAsync(
                "IWideFacade",
                ["Send"],
                "ISender",
                "ignored",
                false);
        }

        // SenderOnlyConsumer only uses Send — its ctor param and field should be narrowed
        var content = await project.ReadSourceAsync("Consumers.cs");
        content.ShouldContain("ISender", Case.Sensitive);
    }

    // -------------------------------------------------------------------------
    // AC6: leaves mixed-usage consumers unchanged
    // -------------------------------------------------------------------------
    [Fact]
    public async Task leaves_mixed_usage_consumer_unchanged()
    {
        using var project = BuildWideFacadeProject();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<NarrowDependencyCommand>().ExecuteAsync(
                "IWideFacade",
                ["Send"],
                "ISender",
                "ignored",
                false);
        }

        // MixedConsumer uses Send + Receive — should still have IWideFacade
        var content = await project.ReadSourceAsync("Consumers.cs");
        content.ShouldContain("IWideFacade", Case.Sensitive);
    }

    // -------------------------------------------------------------------------
    // AC8: invalid member causes validation error with no changes
    // -------------------------------------------------------------------------
    [Fact]
    public async Task fails_with_clear_error_when_member_not_on_interface()
    {
        using var project = BuildWideFacadeProject();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _services.Resolve<NarrowDependencyCommand>().ExecuteAsync(
                    "IWideFacade",
                    ["NonExistentMethod"],
                    "INarrow",
                    "ignored",
                    false));

            ex.Message.ShouldContain("NonExistentMethod", Case.Sensitive);
            ex.Message.ShouldContain("IWideFacade", Case.Sensitive);
        }

        // No files written
        project.SourceExists("INarrow.cs").ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // AC10: --dry-run prints diff, writes no files
    // -------------------------------------------------------------------------
    [Fact]
    public async Task handles_dry_run_does_not_write_files()
    {
        using var project = BuildWideFacadeProject();
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<NarrowDependencyCommand>().ExecuteAsync(
                "IWideFacade",
                ["Send"],
                "ISender",
                "ignored",
                true);
        }

        Assert.False(project.SourceExists("ISender.cs"),
            "ISender.cs should NOT be written during dry-run");
    }

    // -------------------------------------------------------------------------
    // AC7: DI follow-up is reported when consumers were narrowed
    // -------------------------------------------------------------------------
    [Fact]
    public async Task reports_di_follow_up_when_consumers_narrowed()
    {
        using var project = BuildWideFacadeProject();
        var (workspace, sln) = project.Build();

        var originalOut = Console.Out;
        using var stdout = new StringWriter();
        Console.SetOut(stdout);
        try
        {
            using (WorkspaceLoader.UseOverride(workspace, sln))
            {
                await _services.Resolve<NarrowDependencyCommand>().ExecuteAsync(
                    "IWideFacade",
                    ["Send"],
                    "ISender",
                    "ignored",
                    false);
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = stdout.ToString();
        Assert.Contains("DI", output, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // AC9: consumer skipped with explicit reason when usage exceeds subset
    // -------------------------------------------------------------------------
    [Fact]
    public async Task reports_skipped_consumers_that_exceed_selected_subset()
    {
        using var project = BuildWideFacadeProject();
        var (workspace, sln) = project.Build();

        var originalOut = Console.Out;
        using var stdout = new StringWriter();
        Console.SetOut(stdout);
        try
        {
            using (WorkspaceLoader.UseOverride(workspace, sln))
            {
                await _services.Resolve<NarrowDependencyCommand>().ExecuteAsync(
                    "IWideFacade",
                    ["Send"],
                    "ISender",
                    "ignored",
                    false);
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        // MixedConsumer uses Send + Receive, so it should be skipped
        var output = stdout.ToString();
        output.ShouldContain("MixedConsumer", Case.Sensitive);
        Assert.Contains("skipped", output, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Command registration
    // -------------------------------------------------------------------------
    [Fact]
    public void registers_command_with_expected_options()
    {
        App.Commands.ShouldContain(entry => entry.Info.Name == "narrow-dependency");

        var command = _services.Resolve<NarrowDependencyCommand>().Build();

        command.Arguments.ShouldContain(arg => arg.Name == "interface");
        command.Options.ShouldContain(opt => opt.Name == "members");
        command.Options.ShouldContain(opt => opt.Name == "new-name");
        command.Options.ShouldContain(opt => opt.Name == "solution");
        command.Options.ShouldContain(opt => opt.Name == "dry-run");
    }

    // -------------------------------------------------------------------------
    // AC3: reuses existing interface when shape already matches
    // -------------------------------------------------------------------------
    [Fact]
    public async Task reuses_existing_interface_when_shape_matches()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("IWideFacade.cs", """
                                            namespace MyApp;

                                            public interface IWideFacade
                                            {
                                                void Send(string message);
                                                void Receive();
                                            }
                                            """);
        // Pre-existing ISender with the exact same member
        project.AddSource("ISender.cs", """
                                        namespace MyApp;

                                        public interface ISender
                                        {
                                            void Send(string message);
                                        }
                                        """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<NarrowDependencyCommand>().ExecuteAsync(
                "IWideFacade",
                ["Send"],
                "ISender",
                "ignored",
                false);
        }

        // No exception thrown — existing ISender was reused.
        // Content should be unchanged (not overwritten with a duplicate)
        var content = await project.ReadSourceAsync("ISender.cs");
        content.ShouldContain("ISender", Case.Sensitive);
        content.ShouldContain("Send", Case.Sensitive);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static AdhocProjectBuilder BuildWideFacadeProject()
    {
        var project = new AdhocProjectBuilder();

        project.AddSource("IWideFacade.cs", """
                                            namespace MyApp;

                                            public interface IWideFacade
                                            {
                                                void Send(string message);
                                                void Receive();
                                                void Delete(int id);
                                            }
                                            """);

        project.AddSource("Consumers.cs", """
                                          namespace MyApp;

                                          /// Only uses Send — safe to narrow
                                          public sealed class SenderOnlyConsumer
                                          {
                                              private readonly IWideFacade _facade;

                                              public SenderOnlyConsumer(IWideFacade facade)
                                              {
                                                  _facade = facade;
                                              }

                                              public void Use()
                                              {
                                                  _facade.Send("hello");
                                              }
                                          }

                                          /// Uses Send + Receive — must stay on IWideFacade
                                          public sealed class MixedConsumer
                                          {
                                              private readonly IWideFacade _facade;

                                              public MixedConsumer(IWideFacade facade)
                                              {
                                                  _facade = facade;
                                              }

                                              public void Use()
                                              {
                                                  _facade.Send("hello");
                                                  _facade.Receive();
                                              }
                                          }
                                          """);

        return project;
    }
}
