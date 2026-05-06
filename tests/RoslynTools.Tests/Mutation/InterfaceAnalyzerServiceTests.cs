using Microsoft.CodeAnalysis;
using RoslynTools.Services;

namespace RoslynTools.Tests.Mutation;

public sealed class InterfaceAnalyzerServiceTests
{
    // -------------------------------------------------------------------------
    // AC: GetUsedMembersAsync returns only the members the consumer actually uses
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetUsedMembersAsync_returns_only_members_actually_called()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("IWideFacade.cs", """
            namespace MyApp;

            public interface IWideFacade
            {
                void Send(string message);
                void Receive();
                void Delete(int id);
            }
            """);
        project.AddSource("SenderOnly.cs", """
            namespace MyApp;

            public sealed class SenderOnly
            {
                private readonly IWideFacade _facade;
                public SenderOnly(IWideFacade facade) { _facade = facade; }
                public void Use() { _facade.Send("hello"); }
            }
            """);
        var (workspace, sln) = project.Build();

        // Find symbols
        INamedTypeSymbol? interfaceSymbol = null;
        INamedTypeSymbol? consumerSymbol = null;
        foreach (var proj in sln.Projects)
        {
            var compilation = await proj.GetCompilationAsync();
            if (compilation is null) continue;
            interfaceSymbol ??= compilation.GetTypeByMetadataName("MyApp.IWideFacade");
            consumerSymbol ??= compilation.GetTypeByMetadataName("MyApp.SenderOnly");
        }

        Assert.NotNull(interfaceSymbol);
        Assert.NotNull(consumerSymbol);

        var used = await InterfaceAnalyzerService.GetUsedMembersAsync(sln, consumerSymbol, interfaceSymbol);

        Assert.Contains("Send", used);
        Assert.DoesNotContain("Receive", used);
        Assert.DoesNotContain("Delete", used);
    }

    // -------------------------------------------------------------------------
    // AC: BuildInterfaceText generates syntactically correct interface source
    // -------------------------------------------------------------------------
    [Fact]
    public async Task BuildInterfaceText_generates_interface_with_selected_members_only()
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
        var (workspace, sln) = project.Build();

        INamedTypeSymbol? interfaceSymbol = null;
        foreach (var proj in sln.Projects)
        {
            var compilation = await proj.GetCompilationAsync();
            if (compilation is null) continue;
            interfaceSymbol ??= compilation.GetTypeByMetadataName("MyApp.IWideFacade");
        }

        Assert.NotNull(interfaceSymbol);

        var sendMember = interfaceSymbol.GetMembers("Send").First();
        var text = InterfaceAnalyzerService.BuildInterfaceText("MyApp", "ISender", [sendMember]);

        Assert.Contains("ISender", text);
        Assert.Contains("Send", text);
        Assert.DoesNotContain("Receive", text);
    }
}
