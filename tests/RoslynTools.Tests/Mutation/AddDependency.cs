using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class AddDependency : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public AddDependency(RtServicesFixture services) => _services = services;

    // ──────────────────────────────────────────────────────────────────────────
    //  1. Basic field + ctor param injection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task adds_a_field_and_constructor_parameter()
    {
        using var proj = new AdhocProjectBuilder();
        proj.AddSource("AgentService.cs", """
                                          public interface IEmailService { void Send(); }

                                          public class AgentService
                                          {
                                              private readonly string _name;
                                              public AgentService(string name)
                                              {
                                                  _name = name;
                                              }
                                              public void DoWork() { }
                                          }
                                          """);
        proj.AddSource("NoCtor.cs", """
                                    public interface ILogService { void Log(string msg); }
                                    public class NoCtor { }
                                    """);
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            await _services.Resolve<AddDependencyCommand>().ExecuteCoreAsync(
                sln,
                "AgentService",
                "IEmailService",
                null,
                false);
        }

        var content = await proj.ReadSourceAsync("AgentService.cs");

        content.ShouldContain("private readonly IEmailService _emailService", Case.Sensitive);
        content.ShouldContain("IEmailService emailService", Case.Sensitive);
        content.ShouldContain("_emailService = emailService;", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. Custom field name override
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task uses_the_specified_field_name()
    {
        using var proj = new AdhocProjectBuilder();
        proj.AddSource("AgentService.cs", """
                                          public interface IEmailService { void Send(); }

                                          public class AgentService
                                          {
                                              private readonly string _name;
                                              public AgentService(string name)
                                              {
                                                  _name = name;
                                              }
                                              public void DoWork() { }
                                          }
                                          """);
        proj.AddSource("NoCtor.cs", """
                                    public interface ILogService { void Log(string msg); }
                                    public class NoCtor { }
                                    """);
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            await _services.Resolve<AddDependencyCommand>().ExecuteCoreAsync(
                sln,
                "AgentService",
                "IEmailService",
                "_mailer",
                false);
        }

        var content = await proj.ReadSourceAsync("AgentService.cs");

        content.ShouldContain("private readonly IEmailService _mailer", Case.Sensitive);
        content.ShouldContain("IEmailService mailer", Case.Sensitive);
        content.ShouldContain("_mailer = mailer;", Case.Sensitive);
        content.ShouldNotContain("_emailService", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. No constructor — generates one
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task generates_a_constructor_when_none_exists()
    {
        using var proj = new AdhocProjectBuilder();
        proj.AddSource("AgentService.cs", """
                                          public interface IEmailService { void Send(); }

                                          public class AgentService
                                          {
                                              private readonly string _name;
                                              public AgentService(string name)
                                              {
                                                  _name = name;
                                              }
                                              public void DoWork() { }
                                          }
                                          """);
        proj.AddSource("NoCtor.cs", """
                                    public interface ILogService { void Log(string msg); }
                                    public class NoCtor { }
                                    """);
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            await _services.Resolve<AddDependencyCommand>().ExecuteCoreAsync(
                sln,
                "NoCtor",
                "ILogService",
                null,
                false);
        }

        var content = await proj.ReadSourceAsync("NoCtor.cs");

        content.ShouldContain("private readonly ILogService _logService", Case.Sensitive);
        content.ShouldContain("public NoCtor(ILogService logService)", Case.Sensitive);
        content.ShouldContain("_logService = logService;", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. Dry-run — no file writes
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task does_not_modify_files()
    {
        using var proj = new AdhocProjectBuilder();
        proj.AddSource("AgentService.cs", """
                                          public interface IEmailService { void Send(); }

                                          public class AgentService
                                          {
                                              private readonly string _name;
                                              public AgentService(string name)
                                              {
                                                  _name = name;
                                              }
                                              public void DoWork() { }
                                          }
                                          """);
        proj.AddSource("NoCtor.cs", """
                                    public interface ILogService { void Log(string msg); }
                                    public class NoCtor { }
                                    """);
        var (workspace, sln) = proj.Build();
        var originalContent = await proj.ReadSourceAsync("AgentService.cs");

        using (workspace)
        {
            await _services.Resolve<AddDependencyCommand>().ExecuteCoreAsync(
                sln,
                "AgentService",
                "IEmailService",
                null,
                true);
        }

        var afterContent = await proj.ReadSourceAsync("AgentService.cs");
        afterContent.ShouldBe(originalContent);
    }
}
