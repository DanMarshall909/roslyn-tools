using Microsoft.CodeAnalysis;
using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class ChangeSignature : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public ChangeSignature(RtServicesFixture services) => _services = services;

    [Fact]
    public async Task renames_a_parameter_and_updates_named_arguments()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int count)
                                            {
                                            }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;

                                       public class Caller
                                       {
                                           public void Run(Service service)
                                           {
                                               service.Foo(count: 5);
                                               service.Foo(5);
                                           }
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().RenameParamAsync(
                solution,
                "TestApp.Service.Foo",
                "count",
                "total");

            var serviceText = await GetDocumentTextAsync(updated, "Service.cs");
            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");

            serviceText.ShouldContain("Foo(int total)", Case.Sensitive);
            callerText.ShouldContain("Foo(total: 5)", Case.Sensitive);
            callerText.ShouldContain("Foo(5)", Case.Sensitive);
        }
    }

    [Fact]
    public async Task updates_multiple_named_argument_call_sites_across_documents()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int count)
                                            {
                                            }
                                        }
                                        """);
        project.AddSource("Caller1.cs", """
                                        namespace TestApp;

                                        public class Caller1
                                        {
                                            public void Run(Service service)
                                            {
                                                service.Foo(count: 1);
                                            }
                                        }
                                        """);
        project.AddSource("Caller2.cs", """
                                        namespace TestApp;

                                        public class Caller2
                                        {
                                            public void Run(Service service)
                                            {
                                                service.Foo(count: 2);
                                            }
                                        }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().RenameParamAsync(
                solution,
                "TestApp.Service.Foo",
                "count",
                "total");

            (await GetDocumentTextAsync(updated, "Service.cs")).ShouldContain("Foo(int total)", Case.Sensitive);
            (await GetDocumentTextAsync(updated, "Caller1.cs")).ShouldContain("Foo(total: 1)", Case.Sensitive);
            (await GetDocumentTextAsync(updated, "Caller2.cs")).ShouldContain("Foo(total: 2)", Case.Sensitive);
        }
    }

    [Fact]
    public async Task renames_constructor_parameters_and_named_object_creation_arguments()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public class Widget
                                       {
                                           public Widget(int count)
                                           {
                                           }
                                       }
                                       """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;

                                       public class Caller
                                       {
                                           public void Run()
                                           {
                                               _ = new Widget(count: 5);
                                           }
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().RenameParamAsync(
                solution,
                "TestApp.Widget.Widget",
                "count",
                "total");

            (await GetDocumentTextAsync(updated, "Widget.cs")).ShouldContain("Widget(int total)", Case.Sensitive);
            (await GetDocumentTextAsync(updated, "Caller.cs")).ShouldContain("new Widget(total: 5)", Case.Sensitive);
        }
    }

    [Fact]
    public async Task renames_local_function_parameters_and_named_arguments_within_scope()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Container.cs", """
                                          namespace TestApp;

                                          public class Container
                                          {
                                              public void Run()
                                              {
                                                  void Local(int count)
                                                  {
                                                  }

                                                  Local(count: 5);
                                              }
                                          }
                                          """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().RenameParamAsync(
                solution,
                "TestApp.Container.Run.Local",
                "count",
                "total");

            var text = await GetDocumentTextAsync(updated, "Container.cs");
            text.ShouldContain("Local(int total)", Case.Sensitive);
            text.ShouldContain("Local(total: 5)", Case.Sensitive);
        }
    }

    [Fact]
    public async Task throws_when_the_parameter_is_missing()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int count)
                                            {
                                            }
                                        }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ChangeSignatureService().RenameParamAsync(solution, "TestApp.Service.Foo", "missing", "total"));

            ex.Message.ShouldContain("missing", Case.Sensitive);
        }
    }

    [Fact]
    public async Task throws_when_the_new_name_conflicts_with_another_parameter()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int count, int total)
                                            {
                                            }
                                        }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ChangeSignatureService().RenameParamAsync(solution, "TestApp.Service.Foo", "count", "total"));

            ex.Message.ShouldContain("already has a parameter", Case.Sensitive);
        }
    }

    [Fact]
    public async Task ambiguous_overloads_require_decision_store_or_specific_signature()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int count)
                                            {
                                            }

                                            public void Foo(string name)
                                            {
                                            }
                                        }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ChangeSignatureService().RenameParamAsync(
                    solution,
                    "TestApp.Service.Foo",
                    "count",
                    "total"));

            ex.Message.ShouldContain("Multiple symbols match 'TestApp.Service.Foo'", Case.Sensitive);
            ex.Message.ShouldContain("TestApp.Service.Foo(int)", Case.Sensitive);
            ex.Message.ShouldContain("TestApp.Service.Foo(string)", Case.Sensitive);
        }
    }

    [Fact]
    public async Task ambiguous_overload_records_decision_entry_without_tty_prompt()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int count)
                                            {
                                            }

                                            public void Foo(string name)
                                            {
                                            }
                                        }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var store = Core.Decisions.DecisionStore.Empty();
            Core.Decisions.DecisionContext.Store = store;
            try
            {
                await Assert.ThrowsAsync<Core.Decisions.DecisionsRequiredException>(() =>
                    new ChangeSignatureService().RenameParamAsync(
                        solution,
                        "TestApp.Service.Foo",
                        "count",
                        "total"));

                store.TryGetAnswer("ambig-TestApp.Service.Foo-2", out _).ShouldBeFalse();
            }
            finally
            {
                Core.Decisions.DecisionContext.Store = null;
            }
        }
    }

    [Fact]
    public void registers_the_rename_param_subcommand_and_metadata()
    {
        ChangeSignatureCommand.Info.Name.ShouldBe("change-signature");
        ChangeSignatureCommand.Info.Mutates.ShouldBeTrue();
        ChangeSignatureCommand.Info.JsonOutput.ShouldBeFalse();
        ChangeSignatureCommand.Info.Examples.ShouldContain(example => example.Contains("rename-param", StringComparison.Ordinal));

        var command = _services.Resolve<ChangeSignatureCommand>().Build();
        var renameParam = command.Subcommands.Single(subcommand => subcommand.Name == "rename-param");

        command.Name.ShouldBe("change-signature");
        renameParam.Arguments.Select(argument => argument.Name).ShouldBe(["methodSpec", "oldName", "newName"]);
        renameParam.Options.Select(option => option.Name).OrderBy(name => name, StringComparer.Ordinal).ShouldBe(["dry-run", "solution"]);
    }

    [Fact]
    public async Task handles_dry_run_reports_changed_files_and_does_not_modify_files()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int count)
                                            {
                                            }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;

                                       public class Caller
                                       {
                                           public void Run(Service service)
                                           {
                                               service.Foo(count: 5);
                                           }
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        var beforeService = await project.ReadSourceAsync("Service.cs");
        var beforeCaller = await project.ReadSourceAsync("Caller.cs");

        using (workspace)
        using (WorkspaceLoader.UseTrustedOverride(workspace, solution, project.Directory))
        {
            var originalOut = Console.Out;
            using var output = new StringWriter();
            Console.SetOut(output);

            try
            {
                var exitCode = await App.RunAsync(
                [
                    "change-signature",
                    "rename-param",
                    "TestApp.Service.Foo",
                    "count",
                    "total",
                    "--solution",
                    "ignored",
                    "--dry-run"
                ]);

                exitCode.ShouldBe(0);
                output.ToString().ShouldContain("\"changedFiles\"", Case.Sensitive);
                output.ToString().ShouldContain("Service.cs", Case.Sensitive);
                output.ToString().ShouldContain("Caller.cs", Case.Sensitive);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        (await project.ReadSourceAsync("Service.cs")).ShouldBe(beforeService);
        (await project.ReadSourceAsync("Caller.cs")).ShouldBe(beforeCaller);
    }

    [Fact]
    public async Task applies_changes_to_disk_when_explicitly_requested()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int count)
                                            {
                                            }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;

                                       public class Caller
                                       {
                                           public void Run(Service service)
                                           {
                                               service.Foo(count: 5);
                                           }
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseTrustedOverride(workspace, solution, project.Directory))
        {
            var exitCode = await App.RunAsync(
            [
                "change-signature",
                "rename-param",
                "TestApp.Service.Foo",
                "count",
                "total",
                "--solution",
                Path.Combine(project.Directory, "fixture.sln"),
                "--trust-repository",
                "--dry-run",
                "false"
            ]);

            exitCode.ShouldBe(0);
        }

        (await project.ReadSourceAsync("Service.cs")).ShouldContain("Foo(int total)", Case.Sensitive);
        (await project.ReadSourceAsync("Caller.cs")).ShouldContain("Foo(total: 5)", Case.Sensitive);
    }

    [Fact]
    public async Task unresolved_method_specs_exit_non_zero_and_name_the_spec()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int count)
                                            {
                                            }
                                        }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseTrustedOverride(workspace, solution, project.Directory))
        {
            var originalError = Console.Error;
            using var error = new StringWriter();
            Console.SetError(error);

            try
            {
                Environment.ExitCode = 0;

                var exitCode = await App.RunAsync(
                [
                    "change-signature",
                    "rename-param",
                    "TestApp.Service.Missing",
                    "count",
                    "total",
                    "--solution",
                    "ignored"
                ]);

                exitCode.ShouldBe(1);
                error.ToString().ShouldContain("TestApp.Service.Missing", Case.Sensitive);
            }
            finally
            {
                Console.SetError(originalError);
                Environment.ExitCode = 0;
            }
        }
    }

    // ---- reorder sub-command registration ----

    [Fact]
    public void registers_the_reorder_subcommand()
    {
        var command = _services.Resolve<ChangeSignatureCommand>().Build();
        var reorder = command.Subcommands.Single(sub => sub.Name == "reorder");

        reorder.Arguments.Select(a => a.Name).ShouldBe(["methodSpec", "order"]);
        reorder.Options.Select(o => o.Name).OrderBy(n => n, StringComparer.Ordinal).ShouldBe(["dry-run", "solution"]);
    }

    // ---- ReorderParamsAsync: validation ----

    [Fact]
    public async Task reorders_params_length_mismatch_throws()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service { public void Foo(int a, int b, int c) { } }
                                        """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ChangeSignatureService().ReorderParamsAsync(solution, "TestApp.Service.Foo", ["1", "0"]));
            ex.Message.ShouldContain("parameter count", Case.Sensitive);
        }
    }

    [Fact]
    public async Task reorders_params_duplicate_index_throws()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service { public void Foo(int a, int b) { } }
                                        """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ChangeSignatureService().ReorderParamsAsync(solution, "TestApp.Service.Foo", ["0", "0"]));
            ex.Message.ShouldContain("duplicate", Case.Sensitive);
        }
    }

    [Fact]
    public async Task reorders_params_out_of_range_index_throws()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service { public void Foo(int a, int b) { } }
                                        """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ChangeSignatureService().ReorderParamsAsync(solution, "TestApp.Service.Foo", ["0", "5"]));
            ex.Message.ShouldContain("out of range", Case.Sensitive);
        }
    }

    [Fact]
    public async Task reorders_params_unknown_name_throws()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service { public void Foo(int a, int b) { } }
                                        """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ChangeSignatureService().ReorderParamsAsync(solution, "TestApp.Service.Foo", ["b", "unknown"]));
            ex.Message.ShouldContain("unknown", Case.Sensitive);
        }
    }

    [Fact]
    public async Task reorders_params_mixed_name_and_index_throws()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service { public void Foo(int a, int b) { } }
                                        """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ChangeSignatureService().ReorderParamsAsync(solution, "TestApp.Service.Foo", ["0", "b"]));
            ex.Message.ShouldContain("mixed", Case.Sensitive);
        }
    }

    [Fact]
    public async Task reorders_params_params_array_rejected()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service { public void Foo(int a, params int[] rest) { } }
                                        """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ChangeSignatureService().ReorderParamsAsync(solution, "TestApp.Service.Foo", ["1", "0"]));
            ex.Message.ShouldContain("params", Case.Sensitive);
        }
    }

    // ---- ReorderParamsAsync: identity reorder ----

    [Fact]
    public async Task reorders_params_identity_produces_no_changes()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service
                                        {
                                            public void Foo(int a, int b, int c) { }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;
                                       public class Caller
                                       {
                                           public void Run(Service s) { s.Foo(1, 2, 3); }
                                       }
                                       """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().ReorderParamsAsync(
                solution, "TestApp.Service.Foo", ["0", "1", "2"]);
            var changes = updated.GetChanges(solution);
            changes.GetProjectChanges().SelectMany(p => p.GetChangedDocuments()).ShouldBeEmpty();
        }
    }

    // ---- ReorderParamsAsync: numeric-index reorder ----

    [Fact]
    public async Task reorders_params_numeric_reorders_declaration_and_positional_callsite()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service
                                        {
                                            public void Foo(int a, int b, int c) { }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;
                                       public class Caller
                                       {
                                           public void Run(Service s) { s.Foo(1, 2, 3); }
                                       }
                                       """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().ReorderParamsAsync(
                solution, "TestApp.Service.Foo", ["2", "0", "1"]);

            var svc = await GetDocumentTextAsync(updated, "Service.cs");
            var caller = await GetDocumentTextAsync(updated, "Caller.cs");

            svc.ShouldContain("Foo(int c, int a, int b)", Case.Sensitive);
            caller.ShouldContain("Foo(3, 1, 2)", Case.Sensitive);
        }
    }

    // ---- ReorderParamsAsync: name-based reorder ----

    [Fact]
    public async Task reorders_params_name_based_reorders_declaration_and_positional_callsite()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service
                                        {
                                            public void Foo(int a, int b, int c) { }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;
                                       public class Caller
                                       {
                                           public void Run(Service s) { s.Foo(1, 2, 3); }
                                       }
                                       """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().ReorderParamsAsync(
                solution, "TestApp.Service.Foo", ["c", "a", "b"]);

            var svc = await GetDocumentTextAsync(updated, "Service.cs");
            var caller = await GetDocumentTextAsync(updated, "Caller.cs");

            svc.ShouldContain("Foo(int c, int a, int b)", Case.Sensitive);
            caller.ShouldContain("Foo(3, 1, 2)", Case.Sensitive);
        }
    }

    // ---- ReorderParamsAsync: named-argument call sites ----

    [Fact]
    public async Task reorders_params_named_arguments_reordered_in_source()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service
                                        {
                                            public void Foo(int a, int b) { }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;
                                       public class Caller
                                       {
                                           public void Run(Service s) { s.Foo(a: 1, b: 2); }
                                       }
                                       """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().ReorderParamsAsync(
                solution, "TestApp.Service.Foo", ["1", "0"]);

            var svc = await GetDocumentTextAsync(updated, "Service.cs");
            var caller = await GetDocumentTextAsync(updated, "Caller.cs");

            svc.ShouldContain("Foo(int b, int a)", Case.Sensitive);
            caller.ShouldContain("Foo(b: 2, a: 1)", Case.Sensitive);
        }
    }

    // ---- ReorderParamsAsync: mixed positional+named call sites ----

    [Fact]
    public async Task reorders_params_mixed_positional_and_named_callsite_rewritten_correctly()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service
                                        {
                                            public void Foo(int a, int b, int c) { }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;
                                       public class Caller
                                       {
                                           public void Run(Service s) { s.Foo(1, b: 2, c: 3); }
                                       }
                                       """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().ReorderParamsAsync(
                solution, "TestApp.Service.Foo", ["2", "0", "1"]);

            var caller = await GetDocumentTextAsync(updated, "Caller.cs");
            // new order is c,a,b; call had positional a=1, named b=2, named c=3
            caller.ShouldContain("Foo(3, 1, b: 2)", Case.Sensitive);
        }
    }

    // ---- ReorderParamsAsync: multiple call sites in same document ----

    [Fact]
    public async Task reorders_params_multiple_callsites_in_same_document()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service
                                        {
                                            public void Foo(int a, int b) { }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;
                                       public class Caller
                                       {
                                           public void Run(Service s)
                                           {
                                               s.Foo(1, 2);
                                               s.Foo(3, 4);
                                           }
                                       }
                                       """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().ReorderParamsAsync(
                solution, "TestApp.Service.Foo", ["1", "0"]);

            var caller = await GetDocumentTextAsync(updated, "Caller.cs");
            caller.ShouldContain("Foo(2, 1)", Case.Sensitive);
            caller.ShouldContain("Foo(4, 3)", Case.Sensitive);
        }
    }

    // ---- ReorderParamsAsync: cross-file call sites ----

    [Fact]
    public async Task reorders_params_cross_file_callsites_updated()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service
                                        {
                                            public void Foo(int a, int b) { }
                                        }
                                        """);
        project.AddSource("Caller1.cs", """
                                        namespace TestApp;
                                        public class Caller1 { public void Run(Service s) { s.Foo(10, 20); } }
                                        """);
        project.AddSource("Caller2.cs", """
                                        namespace TestApp;
                                        public class Caller2 { public void Run(Service s) { s.Foo(30, 40); } }
                                        """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().ReorderParamsAsync(
                solution, "TestApp.Service.Foo", ["1", "0"]);

            (await GetDocumentTextAsync(updated, "Service.cs")).ShouldContain("Foo(int b, int a)", Case.Sensitive);
            (await GetDocumentTextAsync(updated, "Caller1.cs")).ShouldContain("Foo(20, 10)", Case.Sensitive);
            (await GetDocumentTextAsync(updated, "Caller2.cs")).ShouldContain("Foo(40, 30)", Case.Sensitive);
        }
    }

    // ---- ReorderParamsAsync: constructor ----

    [Fact]
    public async Task reorders_params_constructor_updates_declaration_and_object_creation()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;
                                       public class Widget
                                       {
                                           public Widget(int a, int b) { }
                                       }
                                       """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;
                                       public class Caller
                                       {
                                           public void Run() { _ = new Widget(1, 2); }
                                       }
                                       """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().ReorderParamsAsync(
                solution, "TestApp.Widget.Widget", ["1", "0"]);

            (await GetDocumentTextAsync(updated, "Widget.cs")).ShouldContain("Widget(int b, int a)", Case.Sensitive);
            (await GetDocumentTextAsync(updated, "Caller.cs")).ShouldContain("new Widget(2, 1)", Case.Sensitive);
        }
    }

    // ---- ReorderParamsAsync: local function ----

    [Fact]
    public async Task reorders_params_local_function_updates_declaration_and_invocations()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Container.cs", """
                                          namespace TestApp;
                                          public class Container
                                          {
                                              public void Run()
                                              {
                                                  void Local(int a, int b) { }
                                                  Local(1, 2);
                                              }
                                          }
                                          """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().ReorderParamsAsync(
                solution, "TestApp.Container.Run.Local", ["1", "0"]);

            var text = await GetDocumentTextAsync(updated, "Container.cs");
            text.ShouldContain("Local(int b, int a)", Case.Sensitive);
            text.ShouldContain("Local(2, 1)", Case.Sensitive);
        }
    }

    // ---- ReorderParamsAsync: omitted optional arguments ----

    [Fact]
    public async Task reorders_params_omitted_trailing_optional_call_site_left_unchanged()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service
                                        {
                                            public void Foo(int a, int b, int c = 0) { }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;
                                       public class Caller
                                       {
                                           public void Run(Service s) { s.Foo(1, 2); }
                                       }
                                       """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            // Reorder a and b; c stays at the end (omitted optional — safe to leave)
            var updated = await new ChangeSignatureService().ReorderParamsAsync(
                solution, "TestApp.Service.Foo", ["1", "0", "2"]);

            var svc = await GetDocumentTextAsync(updated, "Service.cs");
            var caller = await GetDocumentTextAsync(updated, "Caller.cs");

            svc.ShouldContain("Foo(int b, int a, int c = 0)", Case.Sensitive);
            caller.ShouldContain("s.Foo(1, 2)", Case.Sensitive); // call site left as-is (c still trailing)
        }
    }

    [Fact]
    public async Task reorders_params_omitted_optional_moved_to_non_trailing_throws()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service
                                        {
                                            public void Foo(int a, int b = 0, int c = 0) { }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;
                                       public class Caller
                                       {
                                           public void Run(Service s) { s.Foo(1); }
                                       }
                                       """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            // Reorder puts b (index 1, currently omitted) before a (index 0)
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ChangeSignatureService().ReorderParamsAsync(solution, "TestApp.Service.Foo", ["1", "0", "2"]));

            ex.Message.ShouldContain("non-trailing position", Case.Sensitive);
        }
    }

    // ── add-param tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task adds_param_appends_at_end_when_no_after_specified()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int a, int b)
                                            {
                                            }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;

                                       public class Caller
                                       {
                                           public void Run(Service svc) => svc.Foo(1, 2);
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().AddParamAsync(
                solution,
                "TestApp.Service.Foo",
                "int count = 0",
                null);

            var serviceText = await GetDocumentTextAsync(updated, "Service.cs");
            // Declaration updated: count appended after b
            serviceText.ShouldContain("Foo(int a, int b, int count = 0)", Case.Sensitive);

            // Call site NOT modified (new param has a default)
            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");
            callerText.ShouldContain("svc.Foo(1, 2)", Case.Sensitive);
        }
    }

    [Fact]
    public async Task adds_param_inserts_after_named_param()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int a, int b, int c)
                                            {
                                            }
                                        }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().AddParamAsync(
                solution,
                "TestApp.Service.Foo",
                "int x = 0",
                "b");

            var serviceText = await GetDocumentTextAsync(updated, "Service.cs");
            serviceText.ShouldContain("Foo(int a, int b, int x = 0, int c)", Case.Sensitive);
        }
    }

    [Fact]
    public async Task adds_param_after_nonexistent_param_throws()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int a)
                                            {
                                            }
                                        }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ChangeSignatureService().AddParamAsync(solution, "TestApp.Service.Foo", "int x = 0", "z"));

            ex.Message.ShouldContain("z", Case.Sensitive);
        }
    }

    [Fact]
    public async Task adds_param_without_default_value_throws()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int a)
                                            {
                                            }
                                        }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ChangeSignatureService().AddParamAsync(solution, "TestApp.Service.Foo", "int count", null));

            ex.Message.ShouldContain("default value", Case.Sensitive);
        }
    }

    [Fact]
    public async Task adds_param_name_conflict_throws()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int a, int b)
                                            {
                                            }
                                        }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ChangeSignatureService().AddParamAsync(solution, "TestApp.Service.Foo", "int a = 0", null));

            ex.Message.ShouldContain("parameter named 'a'", Case.Sensitive);
        }
    }

    [Fact]
    public async Task adds_param_works_for_constructor_target()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public class Widget
                                       {
                                           public Widget(int x)
                                           {
                                           }
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().AddParamAsync(
                solution,
                "TestApp.Widget.Widget",
                "string label = \"\"",
                null);

            var text = await GetDocumentTextAsync(updated, "Widget.cs");
            text.ShouldContain("Widget(int x, string label = \"\")", Case.Sensitive);
        }
    }

    [Fact]
    public async Task adds_param_works_for_local_function_target()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Container.cs", """
                                          namespace TestApp;

                                          public class Container
                                          {
                                              public void Run()
                                              {
                                                  void Local(int a)
                                                  {
                                                  }

                                                  Local(1);
                                              }
                                          }
                                          """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().AddParamAsync(
                solution,
                "TestApp.Container.Run.Local",
                "int b = 0",
                null);

            var text = await GetDocumentTextAsync(updated, "Container.cs");
            text.ShouldContain("Local(int a, int b = 0)", Case.Sensitive);
        }
    }

    // ── remove-param tests ────────────────────────────────────────────────────

    [Fact]
    public async Task removes_param_requires_default_value_without_force()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int a, int b)
                                            {
                                            }
                                        }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ChangeSignatureService().RemoveParamAsync(solution, "TestApp.Service.Foo", "a", false));

            ex.Message.ShouldContain("default value", Case.Sensitive);
        }
    }

    [Fact]
    public async Task removes_param_force_bypasses_default_value_check()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int a, int b)
                                            {
                                            }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;

                                       public class Caller
                                       {
                                           public void Run(Service svc) => svc.Foo(1, 2);
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().RemoveParamAsync(
                solution, "TestApp.Service.Foo", "a", true);

            var serviceText = await GetDocumentTextAsync(updated, "Service.cs");
            serviceText.ShouldNotContain("int a", Case.Sensitive);
            serviceText.ShouldContain("Foo(int b)", Case.Sensitive);

            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");
            callerText.ShouldContain("Foo(2)", Case.Sensitive);
        }
    }

    [Fact]
    public async Task removes_param_removes_positional_arg_from_single_call_site()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int a = 0, int b = 0)
                                            {
                                            }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;

                                       public class Caller
                                       {
                                           public void Run(Service svc) => svc.Foo(1, 2);
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().RemoveParamAsync(
                solution, "TestApp.Service.Foo", "a", false);

            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");
            callerText.ShouldContain("Foo(2)", Case.Sensitive);
        }
    }

    [Fact]
    public async Task removes_param_rewrites_multiple_call_sites_in_same_document()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int a = 0, int b = 0)
                                            {
                                            }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;

                                       public class Caller
                                       {
                                           public void Run(Service svc)
                                           {
                                               svc.Foo(1, 2);
                                               svc.Foo(3, 4);
                                           }
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().RemoveParamAsync(
                solution, "TestApp.Service.Foo", "a", false);

            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");
            callerText.ShouldContain("Foo(2)", Case.Sensitive);
            callerText.ShouldContain("Foo(4)", Case.Sensitive);
        }
    }

    [Fact]
    public async Task removes_param_removes_positional_arg_across_multiple_files()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int a = 0, int b = 0)
                                            {
                                            }
                                        }
                                        """);
        project.AddSource("Caller1.cs", """
                                        namespace TestApp;

                                        public class Caller1
                                        {
                                            public void Run(Service svc) => svc.Foo(1, 2);
                                        }
                                        """);
        project.AddSource("Caller2.cs", """
                                        namespace TestApp;

                                        public class Caller2
                                        {
                                            public void Run(Service svc) => svc.Foo(3, 4);
                                        }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().RemoveParamAsync(
                solution, "TestApp.Service.Foo", "a", false);

            var caller1Text = await GetDocumentTextAsync(updated, "Caller1.cs");
            var caller2Text = await GetDocumentTextAsync(updated, "Caller2.cs");

            caller1Text.ShouldContain("Foo(2)", Case.Sensitive);
            caller2Text.ShouldContain("Foo(4)", Case.Sensitive);
        }
    }

    [Fact]
    public async Task removes_param_removes_named_arg_from_call_site()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int a = 0, int b = 0)
                                            {
                                            }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;

                                       public class Caller
                                       {
                                           public void Run(Service svc) => svc.Foo(a: 1, b: 2);
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().RemoveParamAsync(
                solution, "TestApp.Service.Foo", "a", false);

            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");
            callerText.ShouldContain("Foo(b: 2)", Case.Sensitive);
            callerText.ShouldNotContain("a: 1", Case.Sensitive);
        }
    }

    [Fact]
    public async Task removes_param_handles_mixed_positional_and_named_call_sites()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int a = 0, int b = 0)
                                            {
                                            }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;

                                       public class Caller
                                       {
                                           public void Run(Service svc)
                                           {
                                               svc.Foo(1, 2);
                                               svc.Foo(a: 3, b: 4);
                                           }
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().RemoveParamAsync(
                solution, "TestApp.Service.Foo", "a", false);

            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");
            callerText.ShouldContain("Foo(2)", Case.Sensitive);
            callerText.ShouldContain("Foo(b: 4)", Case.Sensitive);
            callerText.ShouldNotContain("a: 3", Case.Sensitive);
        }
    }

    [Fact]
    public async Task removes_param_throws_when_param_not_found()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int a)
                                            {
                                            }
                                        }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ChangeSignatureService().RemoveParamAsync(solution, "TestApp.Service.Foo", "missing", true));

            ex.Message.ShouldContain("missing", Case.Sensitive);
        }
    }

    [Fact]
    public async Task removes_param_works_for_constructor_target()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public class Widget
                                       {
                                           public Widget(int x = 0, int y = 0)
                                           {
                                           }
                                       }
                                       """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;

                                       public class Caller
                                       {
                                           public void Run() => _ = new Widget(1, 2);
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().RemoveParamAsync(
                solution, "TestApp.Widget.Widget", "x", false);

            var widgetText = await GetDocumentTextAsync(updated, "Widget.cs");
            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");

            widgetText.ShouldContain("Widget(int y = 0)", Case.Sensitive);
            widgetText.ShouldNotContain("int x", Case.Sensitive);
            callerText.ShouldContain("new Widget(2)", Case.Sensitive);
        }
    }

    [Fact]
    public async Task removes_param_works_for_local_function_target()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Container.cs", """
                                          namespace TestApp;

                                          public class Container
                                          {
                                              public void Run()
                                              {
                                                  void Local(int a = 0, int b = 0)
                                                  {
                                                  }

                                                  Local(1, 2);
                                              }
                                          }
                                          """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().RemoveParamAsync(
                solution, "TestApp.Container.Run.Local", "a", false);

            var text = await GetDocumentTextAsync(updated, "Container.cs");
            text.ShouldContain("Local(int b = 0)", Case.Sensitive);
            text.ShouldContain("Local(2)", Case.Sensitive);
        }
    }

    // ── WARN fixes ────────────────────────────────────────────────────────────

    // WARN 4: AddParam empty-list should use SourceText.WithChanges not round-trip
    [Fact]
    public async Task adds_param_into_empty_param_list_uses_with_changes()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo()
                                            {
                                            }
                                        }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().AddParamAsync(
                solution,
                "TestApp.Service.Foo",
                "int count = 0",
                null);

            var text = await GetDocumentTextAsync(updated, "Service.cs");
            text.ShouldContain("Foo(int count = 0)", Case.Sensitive);
        }
    }

    // WARN 5: same-document declaration + call site — "collect against original, apply after" invariant
    [Fact]
    public async Task removes_param_rewrites_declaration_and_call_site_in_same_document()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("All.cs", """
                                    namespace TestApp;

                                    public class Service
                                    {
                                        public void Foo(int a = 0, int b = 0) { }

                                        public void Run()
                                        {
                                            Foo(1, 2);
                                            Foo(3, 4);
                                        }
                                    }
                                    """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().RemoveParamAsync(
                solution, "TestApp.Service.Foo", "a", false);

            var text = await GetDocumentTextAsync(updated, "All.cs");
            text.ShouldContain("Foo(int b = 0)", Case.Sensitive);
            text.ShouldNotContain("int a", Case.Sensitive);
            text.ShouldContain("Foo(2)", Case.Sensitive);
            text.ShouldContain("Foo(4)", Case.Sensitive);
        }
    }

    // WARN 2: extension method — paramIndex must exclude the implicit receiver
    [Fact]
    public async Task removes_param_extension_method_adjusts_arg_index_for_instance_call()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Extensions.cs", """
                                           namespace TestApp;

                                           public static class Extensions
                                           {
                                               public static void Foo(this Service s, int a = 0, int b = 0) { }
                                           }
                                           """);
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service { }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;

                                       public class Caller
                                       {
                                           public void Run(Service svc) => svc.Foo(1, 2);
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            // Remove 'a' (param index 1 on the symbol, arg index 0 at call site)
            var updated = await new ChangeSignatureService().RemoveParamAsync(
                solution, "TestApp.Extensions.Foo", "a", false);

            var extText = await GetDocumentTextAsync(updated, "Extensions.cs");
            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");

            extText.ShouldContain("Foo(this Service s, int b = 0)", Case.Sensitive);
            extText.ShouldNotContain("int a", Case.Sensitive);
            callerText.ShouldContain("svc.Foo(2)", Case.Sensitive); // only b=2 remains, not a=1
        }
    }

    // WARN 1: method-group reference emits warning to stderr and doesn't crash
    [Fact]
    public async Task removes_param_method_group_reference_emits_warning_to_stderr()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Foo(int a = 0, int b = 0) { }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;

                                       public class Caller
                                       {
                                           public void Run(Service svc)
                                           {
                                               System.Action<int, int> fn = svc.Foo;
                                               svc.Foo(1, 2);
                                           }
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var originalError = Console.Error;
            using var stderrCapture = new StringWriter();
            Console.SetError(stderrCapture);

            try
            {
                var updated = await _services.Resolve<ChangeSignatureService>().RemoveParamAsync(
                    solution, "TestApp.Service.Foo", "a", false);

                var stderr = stderrCapture.ToString();
                stderr.ShouldContain("[WARNING] Reference at", Case.Sensitive); // method-group ref warned

                var callerText = await GetDocumentTextAsync(updated, "Caller.cs");
                callerText.ShouldContain("svc.Foo(2)", Case.Sensitive); // invocation still rewritten
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    // WARN 3: virtual override — call sites on overriding class are also rewritten
    [Fact]
    public async Task removes_param_virtual_method_rewrites_override_call_sites()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Base.cs", """
                                     namespace TestApp;

                                     public class Base
                                     {
                                         public virtual void Foo(int a = 0, int b = 0) { }
                                     }
                                     """);
        project.AddSource("Derived.cs", """
                                        namespace TestApp;

                                        public class Derived : Base
                                        {
                                            public override void Foo(int a = 0, int b = 0) { }
                                        }
                                        """);
        project.AddSource("Caller.cs", """
                                       namespace TestApp;

                                       public class Caller
                                       {
                                           public void Run(Base b, Derived d)
                                           {
                                               b.Foo(1, 2);
                                               d.Foo(3, 4);
                                           }
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var updated = await new ChangeSignatureService().RemoveParamAsync(
                solution, "TestApp.Base.Foo", "a", false);

            var baseText = await GetDocumentTextAsync(updated, "Base.cs");
            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");

            baseText.ShouldContain("Foo(int b = 0)", Case.Sensitive);
            callerText.ShouldContain("b.Foo(2)", Case.Sensitive);
            callerText.ShouldContain("d.Foo(4)", Case.Sensitive);
        }
    }

    // ── registration tests ────────────────────────────────────────────────────

    [Fact]
    public void registers_add_param_and_remove_param_subcommands()
    {
        var command = _services.Resolve<ChangeSignatureCommand>().Build();

        var addParam = command.Subcommands.Single(subcommand => subcommand.Name == "add-param");
        var removeParam = command.Subcommands.Single(subcommand => subcommand.Name == "remove-param");

        addParam.Arguments.Select(a => a.Name).ShouldBe(["methodSpec", "paramSpec"]);
        addParam.Options.ShouldContain(o => o.Name == "after");
        addParam.Options.ShouldContain(o => o.Name == "dry-run");
        addParam.Options.ShouldContain(o => o.Name == "solution");

        removeParam.Arguments.Select(a => a.Name).ShouldBe(["methodSpec", "paramName"]);
        removeParam.Options.ShouldContain(o => o.Name == "force");
        removeParam.Options.ShouldContain(o => o.Name == "dry-run");
        removeParam.Options.ShouldContain(o => o.Name == "solution");

        ChangeSignatureCommand.Info.Examples.ShouldContain(e => e.Contains("add-param", StringComparison.Ordinal));
        ChangeSignatureCommand.Info.Examples.ShouldContain(e => e.Contains("remove-param", StringComparison.Ordinal));
    }

    // ── AC24: unresolved methodSpec on add-param / remove-param ──────────────

    [Fact]
    public async Task adds_param_unresolved_method_spec_exits_non_zero()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service { public void Foo(int a) { } }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseTrustedOverride(workspace, solution, project.Directory))
        {
            var originalError = Console.Error;
            using var error = new StringWriter();
            Console.SetError(error);

            try
            {
                var exitCode = await App.RunAsync(
                [
                    "change-signature", "add-param",
                    "TestApp.Service.Missing", "int x = 0",
                    "--solution", "ignored"
                ]);

                exitCode.ShouldBe(1);
                error.ToString().ShouldContain("Missing", Case.Sensitive);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    [Fact]
    public async Task removes_param_unresolved_method_spec_exits_non_zero()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service { public void Foo(int a = 0) { } }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseTrustedOverride(workspace, solution, project.Directory))
        {
            var originalError = Console.Error;
            using var error = new StringWriter();
            Console.SetError(error);

            try
            {
                var exitCode = await App.RunAsync(
                [
                    "change-signature", "remove-param",
                    "TestApp.Service.Missing", "a",
                    "--solution", "ignored"
                ]);

                exitCode.ShouldBe(1);
                error.ToString().ShouldContain("Missing", Case.Sensitive);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    // ── AC26: --dry-run for add-param and remove-param ────────────────────────

    [Fact]
    public async Task adds_param_dry_run_reports_changed_files_and_does_not_write_files()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service { public void Foo(int a) { } }
                                        """);

        var (workspace, solution) = project.Build();
        var before = await project.ReadSourceAsync("Service.cs");

        using (workspace)
        using (WorkspaceLoader.UseTrustedOverride(workspace, solution, project.Directory))
        {
            var originalOut = Console.Out;
            using var output = new StringWriter();
            Console.SetOut(output);

            try
            {
                var exitCode = await App.RunAsync(
                [
                    "change-signature", "add-param",
                    "TestApp.Service.Foo", "int b = 0",
                    "--solution", "ignored", "--dry-run"
                ]);

                exitCode.ShouldBe(0);
                output.ToString().ShouldContain("\"changedFiles\"", Case.Sensitive);
                output.ToString().ShouldContain("Service.cs", Case.Sensitive);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        (await project.ReadSourceAsync("Service.cs")).ShouldBe(before);
    }

    [Fact]
    public async Task removes_param_dry_run_reports_changed_files_and_does_not_write_files()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service { public void Foo(int a = 0) { } }
                                        """);

        var (workspace, solution) = project.Build();
        var before = await project.ReadSourceAsync("Service.cs");

        using (workspace)
        using (WorkspaceLoader.UseTrustedOverride(workspace, solution, project.Directory))
        {
            var originalOut = Console.Out;
            using var output = new StringWriter();
            Console.SetOut(output);

            try
            {
                var exitCode = await App.RunAsync(
                [
                    "change-signature", "remove-param",
                    "TestApp.Service.Foo", "a",
                    "--solution", "ignored", "--dry-run"
                ]);

                exitCode.ShouldBe(0);
                output.ToString().ShouldContain("\"changedFiles\"", Case.Sensitive);
                output.ToString().ShouldContain("Service.cs", Case.Sensitive);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        (await project.ReadSourceAsync("Service.cs")).ShouldBe(before);
    }

    // ── AC27: explicit apply changes files ────────────────────────────────────

    [Fact]
    public async Task adds_param_applies_changes_to_disk_when_explicitly_requested()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service { public void Foo(int a) { } }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseTrustedOverride(workspace, solution, project.Directory))
        {
            var exitCode = await App.RunAsync(
            [
                "change-signature", "add-param",
                "TestApp.Service.Foo", "int b = 0",
                "--solution", Path.Combine(project.Directory, "fixture.sln"),
                "--trust-repository", "--dry-run", "false"
            ]);

            exitCode.ShouldBe(0);
        }

        (await project.ReadSourceAsync("Service.cs")).ShouldContain("Foo(int a, int b = 0)", Case.Sensitive);
    }

    [Fact]
    public async Task removes_param_applies_changes_to_disk_when_explicitly_requested()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;
                                        public class Service { public void Foo(int a = 0) { } }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseTrustedOverride(workspace, solution, project.Directory))
        {
            var exitCode = await App.RunAsync(
            [
                "change-signature", "remove-param",
                "TestApp.Service.Foo", "a",
                "--solution", Path.Combine(project.Directory, "fixture.sln"),
                "--trust-repository", "--dry-run", "false"
            ]);

            exitCode.ShouldBe(0);
        }

        (await project.ReadSourceAsync("Service.cs")).ShouldContain("Foo()", Case.Sensitive);
    }

    private static async Task<string> GetDocumentTextAsync(Solution solution, string documentName)
    {
        var document = solution.Projects
            .SelectMany(project => project.Documents)
            .Single(doc => doc.Name == documentName);

        var text = await document.GetTextAsync();
        return text.ToString();
    }
}
