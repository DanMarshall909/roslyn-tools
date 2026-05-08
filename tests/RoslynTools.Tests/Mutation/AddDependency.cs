using RoslynTools.Commands;

namespace RoslynTools.Tests.Mutation;

public class AddDependency
{
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
            await AddDependencyCommand.ExecuteCoreAsync(
                sln,
                "AgentService",
                "IEmailService",
                null,
                false);
        }

        var content = await proj.ReadSourceAsync("AgentService.cs");

        Assert.Contains("private readonly IEmailService _emailService", content);
        Assert.Contains("IEmailService emailService", content);
        Assert.Contains("_emailService = emailService;", content);
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
            await AddDependencyCommand.ExecuteCoreAsync(
                sln,
                "AgentService",
                "IEmailService",
                "_mailer",
                false);
        }

        var content = await proj.ReadSourceAsync("AgentService.cs");

        Assert.Contains("private readonly IEmailService _mailer", content);
        Assert.Contains("IEmailService mailer", content);
        Assert.Contains("_mailer = mailer;", content);
        Assert.DoesNotContain("_emailService", content);
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
            await AddDependencyCommand.ExecuteCoreAsync(
                sln,
                "NoCtor",
                "ILogService",
                null,
                false);
        }

        var content = await proj.ReadSourceAsync("NoCtor.cs");

        Assert.Contains("private readonly ILogService _logService", content);
        Assert.Contains("public NoCtor(ILogService logService)", content);
        Assert.Contains("_logService = logService;", content);
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
            await AddDependencyCommand.ExecuteCoreAsync(
                sln,
                "AgentService",
                "IEmailService",
                null,
                true);
        }

        var afterContent = await proj.ReadSourceAsync("AgentService.cs");
        Assert.Equal(originalContent, afterContent);
    }
}
