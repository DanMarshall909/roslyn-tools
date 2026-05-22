using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

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

        interfaceSymbol.ShouldNotBeNull();
        consumerSymbol.ShouldNotBeNull();

        var used = await new Services.InterfaceAnalyzerService().GetUsedMembersAsync(sln, consumerSymbol,
            interfaceSymbol);

        used.ShouldContain("Send");
        used.ShouldNotContain("Receive");
        used.ShouldNotContain("Delete");
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

        interfaceSymbol.ShouldNotBeNull();

        var sendMember = interfaceSymbol.GetMembers("Send").First();
        var text = InterfaceTextBuilder.BuildInterfaceText("MyApp", "ISender", [sendMember]);

        text.ShouldContain("ISender", Case.Sensitive);
        text.ShouldContain("Send", Case.Sensitive);
        text.ShouldNotContain("Receive", Case.Sensitive);
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

            used.ShouldContain("Count");
            used.ShouldContain("Changed");
            used.ShouldNotContain("Reset");
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
            var text = InterfaceTextBuilder.BuildInterfaceText("", "INarrowWidgetEvents", interfaceSymbol.GetMembers().ToList());

            text.ShouldNotContain("namespace", Case.Sensitive);
            text.ShouldContain("public interface INarrowWidgetEvents", Case.Sensitive);
            text.ShouldContain("string Format(int id, string name);", Case.Sensitive);
            text.ShouldContain("int Count { get; set; }", Case.Sensitive);
            text.ShouldContain("event EventHandler Changed;", Case.Sensitive);
        }
    }

    private static async Task<INamedTypeSymbol> GetTypeAsync(Solution solution, string metadataName)
    {
        var compilation = await solution.Projects.Single().GetCompilationAsync();
        return compilation!.GetTypeByMetadataName(metadataName).ShouldBeAssignableTo<INamedTypeSymbol>()!;
    }
}
