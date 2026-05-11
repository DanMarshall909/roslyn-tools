using Microsoft.CodeAnalysis;
using RoslynTools.Services;

namespace RoslynTools.Tests.Mutation;

public sealed class InterfaceAnalyzerService
{
    // -------------------------------------------------------------------------
    // AC: GetUsedMembersAsync returns only the members the consumer actually uses
    // -------------------------------------------------------------------------
    [Fact]
    public async Task gets_used_members_async_returns_only_members_actually_called()
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

        var used = await new Services.InterfaceAnalyzerService().GetUsedMembersAsync(sln, consumerSymbol,
            interfaceSymbol);

        Assert.Contains("Send", used);
        Assert.DoesNotContain("Receive", used);
        Assert.DoesNotContain("Delete", used);
    }

    // -------------------------------------------------------------------------
    // AC: BuildInterfaceText generates syntactically correct interface source
    // -------------------------------------------------------------------------
    [Fact]
    public async Task builds_interface_text_generates_interface_with_selected_members_only()
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
        var text = new Services.InterfaceAnalyzerService().BuildInterfaceText("MyApp", "ISender", [sendMember]);

        Assert.Contains("ISender", text);
        Assert.Contains("Send", text);
        Assert.DoesNotContain("Receive", text);
    }

    [Fact]
    public async Task gets_used_members_async_includes_properties_and_events()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("IWidgetEvents.cs", """
                                              using System;

                                              namespace MyApp;

                                              public interface IWidgetEvents
                                              {
                                                  int Count { get; set; }
                                                  event EventHandler Changed;
                                                  void Reset();
                                              }
                                              """);
        project.AddSource("WidgetConsumer.cs", """
                                               using System;

                                               namespace MyApp;

                                               public sealed class WidgetConsumer
                                               {
                                                   private readonly IWidgetEvents _events;

                                                   public WidgetConsumer(IWidgetEvents events)
                                                   {
                                                       _events = events;
                                                   }

                                                   public void Use()
                                                   {
                                                       _events.Count = _events.Count + 1;
                                                       _events.Changed += OnChanged;
                                                   }

                                                   private void OnChanged(object? sender, EventArgs args)
                                                   {
                                                   }
                                               }
                                               """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var interfaceSymbol = await GetTypeAsync(sln, "MyApp.IWidgetEvents");
            var consumerSymbol = await GetTypeAsync(sln, "MyApp.WidgetConsumer");

            var used = await new Services.InterfaceAnalyzerService()
                .GetUsedMembersAsync(sln, consumerSymbol, interfaceSymbol);

            Assert.Contains("Count", used);
            Assert.Contains("Changed", used);
            Assert.DoesNotContain("Reset", used);
        }
    }

    [Fact]
    public async Task builds_interface_text_includes_methods_properties_and_events()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("IWidgetEvents.cs", """
                                              using System;

                                              namespace MyApp;

                                              public interface IWidgetEvents
                                              {
                                                  string Format(int id, string name);
                                                  int Count { get; set; }
                                                  event EventHandler Changed;
                                              }
                                              """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var interfaceSymbol = await GetTypeAsync(sln, "MyApp.IWidgetEvents");
            var text = new Services.InterfaceAnalyzerService()
                .BuildInterfaceText("", "INarrowWidgetEvents", interfaceSymbol.GetMembers().ToList());

            Assert.DoesNotContain("namespace", text);
            Assert.Contains("public interface INarrowWidgetEvents", text);
            Assert.Contains("string Format(int id, string name);", text);
            Assert.Contains("int Count { get; set; }", text);
            Assert.Contains("event EventHandler Changed;", text);
        }
    }

    private static async Task<INamedTypeSymbol> GetTypeAsync(Solution solution, string metadataName)
    {
        var compilation = await solution.Projects.Single().GetCompilationAsync();
        return Assert.IsAssignableFrom<INamedTypeSymbol>(compilation!.GetTypeByMetadataName(metadataName));
    }
}
