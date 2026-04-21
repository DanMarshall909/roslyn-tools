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

    private static async Task<string> GetDocumentTextAsync(Solution solution, string documentName)
    {
        var document = solution.Projects
            .SelectMany(project => project.Documents)
            .Single(doc => doc.Name == documentName);

        var text = await document.GetTextAsync();
        return text.ToString();
    }
}
