using RoslynTools.Commands;

namespace RoslynTools.Tests;

[Collection("MSBuild")]
public class AddDependencyTests
{
    // ──────────────────────────────────────────────────────────────────────────
    //  1. Basic field + ctor param injection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddDependency_AddsFieldAndCtorParam()
    {
        using var proj = new TestProjectBuilder();
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
        var slnPath = await proj.BuildAsync();

        await AddDependencyCommand.ExecuteAsync(
            typeName: "AgentService",
            depType: "IEmailService",
            fieldName: null,
            solutionPath: slnPath,
            dryRun: false);

        var content = await proj.ReadSourceAsync("AgentService.cs");

        Assert.Contains("private readonly IEmailService _emailService", content);
        Assert.Contains("IEmailService emailService", content);
        Assert.Contains("_emailService = emailService;", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. Custom field name override
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddDependency_CustomFieldName()
    {
        using var proj = new TestProjectBuilder();
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
        var slnPath = await proj.BuildAsync();

        await AddDependencyCommand.ExecuteAsync(
            typeName: "AgentService",
            depType: "IEmailService",
            fieldName: "_mailer",
            solutionPath: slnPath,
            dryRun: false);

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
    public async Task AddDependency_NoConstructor_GeneratesOne()
    {
        using var proj = new TestProjectBuilder();
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
        var slnPath = await proj.BuildAsync();

        await AddDependencyCommand.ExecuteAsync(
            typeName: "NoCtor",
            depType: "ILogService",
            fieldName: null,
            solutionPath: slnPath,
            dryRun: false);

        var content = await proj.ReadSourceAsync("NoCtor.cs");

        Assert.Contains("private readonly ILogService _logService", content);
        Assert.Contains("public NoCtor(ILogService logService)", content);
        Assert.Contains("_logService = logService;", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. Dry-run — no file writes
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddDependency_DryRun_NoWrites()
    {
        using var proj = new TestProjectBuilder();
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
        var slnPath = await proj.BuildAsync();

        var originalContent = await proj.ReadSourceAsync("AgentService.cs");

        await AddDependencyCommand.ExecuteAsync(
            typeName: "AgentService",
            depType: "IEmailService",
            fieldName: null,
            solutionPath: slnPath,
            dryRun: true);

        var afterContent = await proj.ReadSourceAsync("AgentService.cs");
        Assert.Equal(originalContent, afterContent);
    }
}
