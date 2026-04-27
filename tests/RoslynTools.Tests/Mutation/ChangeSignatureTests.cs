using Microsoft.CodeAnalysis;
using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public class ChangeSignatureTests
{
    [Fact]
    public async Task Renames_a_parameter_and_updates_named_arguments()
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
            var updated = await ChangeSignatureCore.RenameParamAsync(
                solution,
                "TestApp.Service.Foo",
                "count",
                "total");

            var serviceText = await GetDocumentTextAsync(updated, "Service.cs");
            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");

            Assert.Contains("Foo(int total)", serviceText);
            Assert.Contains("Foo(total: 5)", callerText);
            Assert.Contains("Foo(5)", callerText);
        }
    }

    [Fact]
    public async Task Updates_multiple_named_argument_call_sites_across_documents()
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
            var updated = await ChangeSignatureCore.RenameParamAsync(
                solution,
                "TestApp.Service.Foo",
                "count",
                "total");

            Assert.Contains("Foo(int total)", await GetDocumentTextAsync(updated, "Service.cs"));
            Assert.Contains("Foo(total: 1)", await GetDocumentTextAsync(updated, "Caller1.cs"));
            Assert.Contains("Foo(total: 2)", await GetDocumentTextAsync(updated, "Caller2.cs"));
        }
    }

    [Fact]
    public async Task Renames_constructor_parameters_and_named_object_creation_arguments()
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
            var updated = await ChangeSignatureCore.RenameParamAsync(
                solution,
                "TestApp.Widget.Widget",
                "count",
                "total");

            Assert.Contains("Widget(int total)", await GetDocumentTextAsync(updated, "Widget.cs"));
            Assert.Contains("new Widget(total: 5)", await GetDocumentTextAsync(updated, "Caller.cs"));
        }
    }

    [Fact]
    public async Task Renames_local_function_parameters_and_named_arguments_within_scope()
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
            var updated = await ChangeSignatureCore.RenameParamAsync(
                solution,
                "TestApp.Container.Run.Local",
                "count",
                "total");

            var text = await GetDocumentTextAsync(updated, "Container.cs");
            Assert.Contains("Local(int total)", text);
            Assert.Contains("Local(total: 5)", text);
        }
    }

    [Fact]
    public async Task Throws_when_the_parameter_is_missing()
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
                ChangeSignatureCore.RenameParamAsync(solution, "TestApp.Service.Foo", "missing", "total"));

            Assert.Contains("missing", ex.Message);
        }
    }

    [Fact]
    public async Task Throws_when_the_new_name_conflicts_with_another_parameter()
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
                ChangeSignatureCore.RenameParamAsync(solution, "TestApp.Service.Foo", "count", "total"));

            Assert.Contains("already has a parameter", ex.Message);
        }
    }

    [Fact]
    public async Task Uses_the_existing_disambiguation_flow_for_overloads()
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
            var originalIn = Console.In;
            var originalOut = Console.Out;
            using var input = new StringReader("0" + Environment.NewLine);
            using var output = new StringWriter();
            Console.SetIn(input);
            Console.SetOut(output);

            try
            {
                var updated = await ChangeSignatureCore.RenameParamAsync(
                    solution,
                    "TestApp.Service.Foo",
                    "count",
                    "total");

                Assert.Contains("Multiple symbols match 'TestApp.Service.Foo'", output.ToString());
                Assert.Contains("Foo(int total)", await GetDocumentTextAsync(updated, "Service.cs"));
            }
            finally
            {
                Console.SetIn(originalIn);
                Console.SetOut(originalOut);
            }
        }
    }

    [Fact]
    public void Registers_the_rename_param_subcommand_and_metadata()
    {
        Assert.Equal("change-signature", ChangeSignatureCommand.Info.Name);
        Assert.True(ChangeSignatureCommand.Info.Mutates);
        Assert.False(ChangeSignatureCommand.Info.JsonOutput);
        Assert.Contains(ChangeSignatureCommand.Info.Examples, example => example.Contains("rename-param", StringComparison.Ordinal));

        var command = ChangeSignatureCommand.Build();
        var renameParam = command.Subcommands.Single(subcommand => subcommand.Name == "rename-param");

        Assert.Equal("change-signature", command.Name);
        Assert.Equal(["methodSpec", "oldName", "newName"], renameParam.Arguments.Select(argument => argument.Name));
        Assert.Equal(
            ["dry-run", "solution"],
            renameParam.Options.Select(option => option.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Dry_run_prints_a_diff_and_does_not_modify_files()
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
        using (WorkspaceLoader.UseOverride(workspace, solution))
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

                Assert.Equal(0, exitCode);
                Assert.Contains("--- ", output.ToString());
                Assert.Contains("+++ ", output.ToString());
                Assert.Contains("@@ ", output.ToString());
                Assert.Contains("[dry-run]", output.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        Assert.Equal(beforeService, await project.ReadSourceAsync("Service.cs"));
        Assert.Equal(beforeCaller, await project.ReadSourceAsync("Caller.cs"));
    }

    [Fact]
    public async Task Applies_changes_to_disk_without_dry_run()
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
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var exitCode = await App.RunAsync(
            [
                "change-signature",
                "rename-param",
                "TestApp.Service.Foo",
                "count",
                "total",
                "--solution",
                "ignored"
            ]);

            Assert.Equal(0, exitCode);
        }

        Assert.Contains("Foo(int total)", await project.ReadSourceAsync("Service.cs"));
        Assert.Contains("Foo(total: 5)", await project.ReadSourceAsync("Caller.cs"));
    }

    [Fact]
    public async Task Unresolved_method_specs_exit_non_zero_and_name_the_spec()
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
        using (WorkspaceLoader.UseOverride(workspace, solution))
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

                Assert.Equal(1, exitCode);
                Assert.Contains("TestApp.Service.Missing", error.ToString());
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
    public void Registers_the_reorder_subcommand()
    {
        var command = ChangeSignatureCommand.Build();
        var reorder = command.Subcommands.Single(sub => sub.Name == "reorder");

        Assert.Equal(["methodSpec", "order"], reorder.Arguments.Select(a => a.Name));
        Assert.Equal(
            ["dry-run", "solution"],
            reorder.Options.Select(o => o.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    // ---- ReorderParamsAsync: validation ----

    [Fact]
    public async Task ReorderParams_length_mismatch_throws()
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
                ChangeSignatureCore.ReorderParamsAsync(solution, "TestApp.Service.Foo", ["1", "0"]));
            Assert.Contains("parameter count", ex.Message);
        }
    }

    [Fact]
    public async Task ReorderParams_duplicate_index_throws()
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
                ChangeSignatureCore.ReorderParamsAsync(solution, "TestApp.Service.Foo", ["0", "0"]));
            Assert.Contains("duplicate", ex.Message);
        }
    }

    [Fact]
    public async Task ReorderParams_out_of_range_index_throws()
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
                ChangeSignatureCore.ReorderParamsAsync(solution, "TestApp.Service.Foo", ["0", "5"]));
            Assert.Contains("out of range", ex.Message);
        }
    }

    [Fact]
    public async Task ReorderParams_unknown_name_throws()
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
                ChangeSignatureCore.ReorderParamsAsync(solution, "TestApp.Service.Foo", ["b", "unknown"]));
            Assert.Contains("unknown", ex.Message);
        }
    }

    [Fact]
    public async Task ReorderParams_mixed_name_and_index_throws()
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
                ChangeSignatureCore.ReorderParamsAsync(solution, "TestApp.Service.Foo", ["0", "b"]));
            Assert.Contains("mixed", ex.Message);
        }
    }

    [Fact]
    public async Task ReorderParams_params_array_rejected()
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
                ChangeSignatureCore.ReorderParamsAsync(solution, "TestApp.Service.Foo", ["1", "0"]));
            Assert.Contains("params", ex.Message);
        }
    }

    // ---- ReorderParamsAsync: identity reorder ----

    [Fact]
    public async Task ReorderParams_identity_produces_no_changes()
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
            var updated = await ChangeSignatureCore.ReorderParamsAsync(
                solution, "TestApp.Service.Foo", ["0", "1", "2"]);
            var changes = updated.GetChanges(solution);
            Assert.Empty(changes.GetProjectChanges().SelectMany(p => p.GetChangedDocuments()));
        }
    }

    // ---- ReorderParamsAsync: numeric-index reorder ----

    [Fact]
    public async Task ReorderParams_numeric_reorders_declaration_and_positional_callsite()
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
            var updated = await ChangeSignatureCore.ReorderParamsAsync(
                solution, "TestApp.Service.Foo", ["2", "0", "1"]);

            var svc = await GetDocumentTextAsync(updated, "Service.cs");
            var caller = await GetDocumentTextAsync(updated, "Caller.cs");

            Assert.Contains("Foo(int c, int a, int b)", svc);
            Assert.Contains("Foo(3, 1, 2)", caller);
        }
    }

    // ---- ReorderParamsAsync: name-based reorder ----

    [Fact]
    public async Task ReorderParams_name_based_reorders_declaration_and_positional_callsite()
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
            var updated = await ChangeSignatureCore.ReorderParamsAsync(
                solution, "TestApp.Service.Foo", ["c", "a", "b"]);

            var svc = await GetDocumentTextAsync(updated, "Service.cs");
            var caller = await GetDocumentTextAsync(updated, "Caller.cs");

            Assert.Contains("Foo(int c, int a, int b)", svc);
            Assert.Contains("Foo(3, 1, 2)", caller);
        }
    }

    // ---- ReorderParamsAsync: named-argument call sites ----

    [Fact]
    public async Task ReorderParams_named_arguments_reordered_in_source()
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
            var updated = await ChangeSignatureCore.ReorderParamsAsync(
                solution, "TestApp.Service.Foo", ["1", "0"]);

            var svc = await GetDocumentTextAsync(updated, "Service.cs");
            var caller = await GetDocumentTextAsync(updated, "Caller.cs");

            Assert.Contains("Foo(int b, int a)", svc);
            Assert.Contains("Foo(b: 2, a: 1)", caller);
        }
    }

    // ---- ReorderParamsAsync: mixed positional+named call sites ----

    [Fact]
    public async Task ReorderParams_mixed_positional_and_named_callsite_rewritten_correctly()
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
            var updated = await ChangeSignatureCore.ReorderParamsAsync(
                solution, "TestApp.Service.Foo", ["2", "0", "1"]);

            var caller = await GetDocumentTextAsync(updated, "Caller.cs");
            // new order is c,a,b; call had positional a=1, named b=2, named c=3
            Assert.Contains("Foo(3, 1, b: 2)", caller);
        }
    }

    // ---- ReorderParamsAsync: multiple call sites in same document ----

    [Fact]
    public async Task ReorderParams_multiple_callsites_in_same_document()
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
            var updated = await ChangeSignatureCore.ReorderParamsAsync(
                solution, "TestApp.Service.Foo", ["1", "0"]);

            var caller = await GetDocumentTextAsync(updated, "Caller.cs");
            Assert.Contains("Foo(2, 1)", caller);
            Assert.Contains("Foo(4, 3)", caller);
        }
    }

    // ---- ReorderParamsAsync: cross-file call sites ----

    [Fact]
    public async Task ReorderParams_cross_file_callsites_updated()
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
            var updated = await ChangeSignatureCore.ReorderParamsAsync(
                solution, "TestApp.Service.Foo", ["1", "0"]);

            Assert.Contains("Foo(int b, int a)", await GetDocumentTextAsync(updated, "Service.cs"));
            Assert.Contains("Foo(20, 10)", await GetDocumentTextAsync(updated, "Caller1.cs"));
            Assert.Contains("Foo(40, 30)", await GetDocumentTextAsync(updated, "Caller2.cs"));
        }
    }

    // ---- ReorderParamsAsync: constructor ----

    [Fact]
    public async Task ReorderParams_constructor_updates_declaration_and_object_creation()
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
            var updated = await ChangeSignatureCore.ReorderParamsAsync(
                solution, "TestApp.Widget.Widget", ["1", "0"]);

            Assert.Contains("Widget(int b, int a)", await GetDocumentTextAsync(updated, "Widget.cs"));
            Assert.Contains("new Widget(2, 1)", await GetDocumentTextAsync(updated, "Caller.cs"));
        }
    }

    // ---- ReorderParamsAsync: local function ----

    [Fact]
    public async Task ReorderParams_local_function_updates_declaration_and_invocations()
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
            var updated = await ChangeSignatureCore.ReorderParamsAsync(
                solution, "TestApp.Container.Run.Local", ["1", "0"]);

            var text = await GetDocumentTextAsync(updated, "Container.cs");
            Assert.Contains("Local(int b, int a)", text);
            Assert.Contains("Local(2, 1)", text);
        }
    }

    // ---- ReorderParamsAsync: omitted optional arguments ----

    [Fact]
    public async Task ReorderParams_omitted_trailing_optional_call_site_left_unchanged()
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
            var updated = await ChangeSignatureCore.ReorderParamsAsync(
                solution, "TestApp.Service.Foo", ["1", "0", "2"]);

            var svc = await GetDocumentTextAsync(updated, "Service.cs");
            var caller = await GetDocumentTextAsync(updated, "Caller.cs");

            Assert.Contains("Foo(int b, int a, int c = 0)", svc);
            Assert.Contains("s.Foo(1, 2)", caller); // call site left as-is (c still trailing)
        }
    }

    [Fact]
    public async Task ReorderParams_omitted_optional_moved_to_non_trailing_throws()
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
                ChangeSignatureCore.ReorderParamsAsync(solution, "TestApp.Service.Foo", ["1", "0", "2"]));

            Assert.Contains("non-trailing position", ex.Message);
        }
    }

    // ── add-param tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task AddParam_appends_at_end_when_no_after_specified()
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
            var updated = await ChangeSignatureCore.AddParamAsync(
                solution,
                "TestApp.Service.Foo",
                "int count = 0",
                afterParam: null);

            var serviceText = await GetDocumentTextAsync(updated, "Service.cs");
            // Declaration updated: count appended after b
            Assert.Contains("Foo(int a, int b, int count = 0)", serviceText);

            // Call site NOT modified (new param has a default)
            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");
            Assert.Contains("svc.Foo(1, 2)", callerText);
        }
    }

    [Fact]
    public async Task AddParam_inserts_after_named_param()
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
            var updated = await ChangeSignatureCore.AddParamAsync(
                solution,
                "TestApp.Service.Foo",
                "int x = 0",
                afterParam: "b");

            var serviceText = await GetDocumentTextAsync(updated, "Service.cs");
            Assert.Contains("Foo(int a, int b, int x = 0, int c)", serviceText);
        }
    }

    [Fact]
    public async Task AddParam_after_nonexistent_param_throws()
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
                ChangeSignatureCore.AddParamAsync(solution, "TestApp.Service.Foo", "int x = 0", afterParam: "z"));

            Assert.Contains("z", ex.Message);
        }
    }

    [Fact]
    public async Task AddParam_without_default_value_throws()
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
                ChangeSignatureCore.AddParamAsync(solution, "TestApp.Service.Foo", "int count", afterParam: null));

            Assert.Contains("default value", ex.Message);
        }
    }

    [Fact]
    public async Task AddParam_name_conflict_throws()
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
                ChangeSignatureCore.AddParamAsync(solution, "TestApp.Service.Foo", "int a = 0", afterParam: null));

            Assert.Contains("parameter named 'a'", ex.Message);
        }
    }

    [Fact]
    public async Task AddParam_works_for_constructor_target()
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
            var updated = await ChangeSignatureCore.AddParamAsync(
                solution,
                "TestApp.Widget.Widget",
                "string label = \"\"",
                afterParam: null);

            var text = await GetDocumentTextAsync(updated, "Widget.cs");
            Assert.Contains("Widget(int x, string label = \"\")", text);
        }
    }

    [Fact]
    public async Task AddParam_works_for_local_function_target()
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
            var updated = await ChangeSignatureCore.AddParamAsync(
                solution,
                "TestApp.Container.Run.Local",
                "int b = 0",
                afterParam: null);

            var text = await GetDocumentTextAsync(updated, "Container.cs");
            Assert.Contains("Local(int a, int b = 0)", text);
        }
    }

    // ── remove-param tests ────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveParam_requires_default_value_without_force()
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
                ChangeSignatureCore.RemoveParamAsync(solution, "TestApp.Service.Foo", "a", force: false));

            Assert.Contains("default value", ex.Message);
        }
    }

    [Fact]
    public async Task RemoveParam_force_bypasses_default_value_check()
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
            var updated = await ChangeSignatureCore.RemoveParamAsync(
                solution, "TestApp.Service.Foo", "a", force: true);

            var serviceText = await GetDocumentTextAsync(updated, "Service.cs");
            Assert.DoesNotContain("int a", serviceText);
            Assert.Contains("Foo(int b)", serviceText);

            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");
            Assert.Contains("Foo(2)", callerText);
        }
    }

    [Fact]
    public async Task RemoveParam_removes_positional_arg_from_single_call_site()
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
            var updated = await ChangeSignatureCore.RemoveParamAsync(
                solution, "TestApp.Service.Foo", "a", force: false);

            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");
            Assert.Contains("Foo(2)", callerText);
        }
    }

    [Fact]
    public async Task RemoveParam_rewrites_multiple_call_sites_in_same_document()
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
            var updated = await ChangeSignatureCore.RemoveParamAsync(
                solution, "TestApp.Service.Foo", "a", force: false);

            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");
            Assert.Contains("Foo(2)", callerText);
            Assert.Contains("Foo(4)", callerText);
        }
    }

    [Fact]
    public async Task RemoveParam_removes_positional_arg_across_multiple_files()
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
            var updated = await ChangeSignatureCore.RemoveParamAsync(
                solution, "TestApp.Service.Foo", "a", force: false);

            var caller1Text = await GetDocumentTextAsync(updated, "Caller1.cs");
            var caller2Text = await GetDocumentTextAsync(updated, "Caller2.cs");

            Assert.Contains("Foo(2)", caller1Text);
            Assert.Contains("Foo(4)", caller2Text);
        }
    }

    [Fact]
    public async Task RemoveParam_removes_named_arg_from_call_site()
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
            var updated = await ChangeSignatureCore.RemoveParamAsync(
                solution, "TestApp.Service.Foo", "a", force: false);

            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");
            Assert.Contains("Foo(b: 2)", callerText);
            Assert.DoesNotContain("a: 1", callerText);
        }
    }

    [Fact]
    public async Task RemoveParam_handles_mixed_positional_and_named_call_sites()
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
            var updated = await ChangeSignatureCore.RemoveParamAsync(
                solution, "TestApp.Service.Foo", "a", force: false);

            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");
            Assert.Contains("Foo(2)", callerText);
            Assert.Contains("Foo(b: 4)", callerText);
            Assert.DoesNotContain("a: 3", callerText);
        }
    }

    [Fact]
    public async Task RemoveParam_throws_when_param_not_found()
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
                ChangeSignatureCore.RemoveParamAsync(solution, "TestApp.Service.Foo", "missing", force: true));

            Assert.Contains("missing", ex.Message);
        }
    }

    [Fact]
    public async Task RemoveParam_works_for_constructor_target()
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
            var updated = await ChangeSignatureCore.RemoveParamAsync(
                solution, "TestApp.Widget.Widget", "x", force: false);

            var widgetText = await GetDocumentTextAsync(updated, "Widget.cs");
            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");

            Assert.Contains("Widget(int y = 0)", widgetText);
            Assert.DoesNotContain("int x", widgetText);
            Assert.Contains("new Widget(2)", callerText);
        }
    }

    [Fact]
    public async Task RemoveParam_works_for_local_function_target()
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
            var updated = await ChangeSignatureCore.RemoveParamAsync(
                solution, "TestApp.Container.Run.Local", "a", force: false);

            var text = await GetDocumentTextAsync(updated, "Container.cs");
            Assert.Contains("Local(int b = 0)", text);
            Assert.Contains("Local(2)", text);
        }
    }

    // ── WARN fixes ────────────────────────────────────────────────────────────

    // WARN 4: AddParam empty-list should use SourceText.WithChanges not round-trip
    [Fact]
    public async Task AddParam_into_empty_param_list_uses_WithChanges()
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
            var updated = await ChangeSignatureCore.AddParamAsync(
                solution,
                "TestApp.Service.Foo",
                "int count = 0",
                afterParam: null);

            var text = await GetDocumentTextAsync(updated, "Service.cs");
            Assert.Contains("Foo(int count = 0)", text);
        }
    }

    // WARN 5: same-document declaration + call site — "collect against original, apply after" invariant
    [Fact]
    public async Task RemoveParam_rewrites_declaration_and_call_site_in_same_document()
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
            var updated = await ChangeSignatureCore.RemoveParamAsync(
                solution, "TestApp.Service.Foo", "a", force: false);

            var text = await GetDocumentTextAsync(updated, "All.cs");
            Assert.Contains("Foo(int b = 0)", text);
            Assert.DoesNotContain("int a", text);
            Assert.Contains("Foo(2)", text);
            Assert.Contains("Foo(4)", text);
        }
    }

    // WARN 2: extension method — paramIndex must exclude the implicit receiver
    [Fact]
    public async Task RemoveParam_extension_method_adjusts_arg_index_for_instance_call()
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
            var updated = await ChangeSignatureCore.RemoveParamAsync(
                solution, "TestApp.Extensions.Foo", "a", force: false);

            var extText = await GetDocumentTextAsync(updated, "Extensions.cs");
            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");

            Assert.Contains("Foo(this Service s, int b = 0)", extText);
            Assert.DoesNotContain("int a", extText);
            Assert.Contains("svc.Foo(2)", callerText); // only b=2 remains, not a=1
        }
    }

    // WARN 1: method-group reference emits warning to stderr and doesn't crash
    [Fact]
    public async Task RemoveParam_method_group_reference_emits_warning_to_stderr()
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
                var updated = await ChangeSignatureCore.RemoveParamAsync(
                    solution, "TestApp.Service.Foo", "a", force: false);

                var stderr = stderrCapture.ToString();
                Assert.Contains("[WARNING] Reference at", stderr); // method-group ref warned

                var callerText = await GetDocumentTextAsync(updated, "Caller.cs");
                Assert.Contains("svc.Foo(2)", callerText); // invocation still rewritten
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    // WARN 3: virtual override — call sites on overriding class are also rewritten
    [Fact]
    public async Task RemoveParam_virtual_method_rewrites_override_call_sites()
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
            var updated = await ChangeSignatureCore.RemoveParamAsync(
                solution, "TestApp.Base.Foo", "a", force: false);

            var baseText = await GetDocumentTextAsync(updated, "Base.cs");
            var callerText = await GetDocumentTextAsync(updated, "Caller.cs");

            Assert.Contains("Foo(int b = 0)", baseText);
            Assert.Contains("b.Foo(2)", callerText);
            Assert.Contains("d.Foo(4)", callerText);
        }
    }

    // ── registration tests ────────────────────────────────────────────────────

    [Fact]
    public void Registers_add_param_and_remove_param_subcommands()
    {
        var command = ChangeSignatureCommand.Build();

        var addParam = command.Subcommands.Single(subcommand => subcommand.Name == "add-param");
        var removeParam = command.Subcommands.Single(subcommand => subcommand.Name == "remove-param");

        Assert.Equal(["methodSpec", "paramSpec"], addParam.Arguments.Select(a => a.Name));
        Assert.Contains(addParam.Options, o => o.Name == "after");
        Assert.Contains(addParam.Options, o => o.Name == "dry-run");
        Assert.Contains(addParam.Options, o => o.Name == "solution");

        Assert.Equal(["methodSpec", "paramName"], removeParam.Arguments.Select(a => a.Name));
        Assert.Contains(removeParam.Options, o => o.Name == "force");
        Assert.Contains(removeParam.Options, o => o.Name == "dry-run");
        Assert.Contains(removeParam.Options, o => o.Name == "solution");

        Assert.Contains(ChangeSignatureCommand.Info.Examples, e => e.Contains("add-param", StringComparison.Ordinal));
        Assert.Contains(ChangeSignatureCommand.Info.Examples, e => e.Contains("remove-param", StringComparison.Ordinal));
    }

    // ── AC24: unresolved methodSpec on add-param / remove-param ──────────────

    [Fact]
    public async Task AddParam_unresolved_method_spec_exits_non_zero()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
            namespace TestApp;
            public class Service { public void Foo(int a) { } }
            """);

        var (workspace, solution) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
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

                Assert.Equal(1, exitCode);
                Assert.Contains("Missing", error.ToString());
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    [Fact]
    public async Task RemoveParam_unresolved_method_spec_exits_non_zero()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
            namespace TestApp;
            public class Service { public void Foo(int a = 0) { } }
            """);

        var (workspace, solution) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
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

                Assert.Equal(1, exitCode);
                Assert.Contains("Missing", error.ToString());
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    // ── AC26: --dry-run for add-param and remove-param ────────────────────────

    [Fact]
    public async Task AddParam_dry_run_prints_diff_and_does_not_write_files()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
            namespace TestApp;
            public class Service { public void Foo(int a) { } }
            """);

        var (workspace, solution) = project.Build();
        var before = await project.ReadSourceAsync("Service.cs");

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
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

                Assert.Equal(0, exitCode);
                Assert.Contains("--- ", output.ToString());
                Assert.Contains("+++ ", output.ToString());
                Assert.Contains("[dry-run]", output.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        Assert.Equal(before, await project.ReadSourceAsync("Service.cs"));
    }

    [Fact]
    public async Task RemoveParam_dry_run_prints_diff_and_does_not_write_files()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
            namespace TestApp;
            public class Service { public void Foo(int a = 0) { } }
            """);

        var (workspace, solution) = project.Build();
        var before = await project.ReadSourceAsync("Service.cs");

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
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

                Assert.Equal(0, exitCode);
                Assert.Contains("--- ", output.ToString());
                Assert.Contains("+++ ", output.ToString());
                Assert.Contains("[dry-run]", output.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        Assert.Equal(before, await project.ReadSourceAsync("Service.cs"));
    }

    // ── AC27: without --dry-run applies changes to disk ───────────────────────

    [Fact]
    public async Task AddParam_applies_changes_to_disk_without_dry_run()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
            namespace TestApp;
            public class Service { public void Foo(int a) { } }
            """);

        var (workspace, solution) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var exitCode = await App.RunAsync(
            [
                "change-signature", "add-param",
                "TestApp.Service.Foo", "int b = 0",
                "--solution", "ignored"
            ]);

            Assert.Equal(0, exitCode);
        }

        Assert.Contains("Foo(int a, int b = 0)", await project.ReadSourceAsync("Service.cs"));
    }

    [Fact]
    public async Task RemoveParam_applies_changes_to_disk_without_dry_run()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
            namespace TestApp;
            public class Service { public void Foo(int a = 0) { } }
            """);

        var (workspace, solution) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var exitCode = await App.RunAsync(
            [
                "change-signature", "remove-param",
                "TestApp.Service.Foo", "a",
                "--solution", "ignored"
            ]);

            Assert.Equal(0, exitCode);
        }

        Assert.Contains("Foo()", await project.ReadSourceAsync("Service.cs"));
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
