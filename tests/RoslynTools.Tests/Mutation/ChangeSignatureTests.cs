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

            Assert.Contains("a", ex.Message);
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

    private static async Task<string> GetDocumentTextAsync(Solution solution, string documentName)
    {
        var document = solution.Projects
            .SelectMany(project => project.Documents)
            .Single(doc => doc.Name == documentName);

        var text = await document.GetTextAsync();
        return text.ToString();
    }
}
