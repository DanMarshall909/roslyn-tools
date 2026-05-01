using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Tests.Mutation;

public class TransformParametersTests
{
    private static string FilePath(AdhocProjectBuilder project, string relativePath)
        => Path.Combine(project.ProjectDir, relativePath);

    // ──────────────────────────────────────────────────────────────────────────
    //  Simple 3-param method: record generated, signature rewritten, call site updated
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Generates_a_record_and_rewrites_the_signature_and_call_site()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("OrderService.cs", """
            namespace TestApp;

            public class OrderService
            {
                public void PlaceOrder(string customerId, string productId, int quantity)
                {
                    _ = customerId + productId + quantity;
                }
            }
            """);
        project.AddSource("Caller.cs", """
            namespace TestApp;

            public class Caller
            {
                public void Run()
                {
                    var svc = new OrderService();
                    svc.PlaceOrder("c1", "p1", 3);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            await TransformParametersService.RunAsync(sln, "OrderService.PlaceOrder", "PlaceOrderArgs", null, dryRun: false);
        }

        var serviceContent = await project.ReadSourceAsync("OrderService.cs");
        var callerContent = await project.ReadSourceAsync("Caller.cs");

        // Record generated in the same file
        Assert.Contains("record PlaceOrderArgs", serviceContent);
        Assert.Contains("string CustomerId", serviceContent);
        Assert.Contains("string ProductId", serviceContent);
        Assert.Contains("int Quantity", serviceContent);

        // Method signature rewritten
        Assert.Contains("PlaceOrder(PlaceOrderArgs args)", serviceContent);
        Assert.DoesNotContain("PlaceOrder(string customerId, string productId, int quantity)", serviceContent);

        // Call site updated
        Assert.Contains("PlaceOrder(new PlaceOrderArgs(\"c1\", \"p1\", 3))", callerContent);
        Assert.DoesNotContain("PlaceOrder(\"c1\", \"p1\", 3)", callerContent);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Default value preserved in record parameter list
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Preserves_default_values_in_the_generated_record()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Greeter.cs", """
            namespace TestApp;

            public class Greeter
            {
                public void Greet(string name, int times = 1)
                {
                    for (int i = 0; i < times; i++)
                        System.Console.WriteLine(name);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            await TransformParametersService.RunAsync(sln, "Greeter.Greet", "GreetArgs", null, dryRun: false);
        }

        var content = await project.ReadSourceAsync("Greeter.cs");

        Assert.Contains("record GreetArgs", content);
        // Default value 1 must appear in the record parameter list
        Assert.Contains("Times = 1", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  out/ref/in parameters → error, skipped params listed
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Throws_a_clear_error_for_out_parameters()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Parser.cs", """
            namespace TestApp;

            public class Parser
            {
                public bool TryParse(string input, out int result)
                {
                    result = 0;
                    return int.TryParse(input, out result);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TransformParametersService.RunAsync(sln, "Parser.TryParse", "ParseArgs", null, dryRun: false));

            Assert.Contains("result", ex.Message);
            Assert.Contains("out/ref/in", ex.Message);
        }
    }

    [Fact]
    public async Task Throws_a_clear_error_for_ref_parameters()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Modifier.cs", """
            namespace TestApp;

            public class Modifier
            {
                public void Increment(ref int counter, int amount)
                {
                    counter += amount;
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TransformParametersService.RunAsync(sln, "Modifier.Increment", "IncrementArgs", null, dryRun: false));

            Assert.Contains("counter", ex.Message);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Multi-call-site update across multiple files
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Updates_every_call_site()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Calculator.cs", """
            namespace TestApp;

            public class Calculator
            {
                public int Add(int x, int y) => x + y;
            }
            """);
        project.AddSource("Client1.cs", """
            namespace TestApp;

            public class Client1
            {
                public void Run()
                {
                    var calc = new Calculator();
                    var r1 = calc.Add(1, 2);
                    var r2 = calc.Add(3, 4);
                }
            }
            """);
        project.AddSource("Client2.cs", """
            namespace TestApp;

            public class Client2
            {
                public void Run()
                {
                    var calc = new Calculator();
                    var r = calc.Add(10, 20);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            await TransformParametersService.RunAsync(sln, "Calculator.Add", "AddArgs", null, dryRun: false);
        }

        var calcContent = await project.ReadSourceAsync("Calculator.cs");
        var client1Content = await project.ReadSourceAsync("Client1.cs");
        var client2Content = await project.ReadSourceAsync("Client2.cs");

        Assert.Contains("record AddArgs", calcContent);
        Assert.Contains("Add(AddArgs args)", calcContent);

        Assert.Contains("Add(new AddArgs(1, 2))", client1Content);
        Assert.Contains("Add(new AddArgs(3, 4))", client1Content);
        Assert.DoesNotContain("Add(1, 2)", client1Content);

        Assert.Contains("Add(new AddArgs(10, 20))", client2Content);
        Assert.DoesNotContain("Add(10, 20)", client2Content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Dry-run emits diff without writing
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Does_not_modify_files()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
            namespace TestApp;

            public class Service
            {
                public void Execute(string name, int value)
                {
                    _ = name + value;
                }
            }
            """);

        var (workspace, sln) = project.Build();
        var originalContent = await project.ReadSourceAsync("Service.cs");
        using (workspace)
        {
            await TransformParametersService.RunAsync(sln, "Service.Execute", "ExecuteArgs", null, dryRun: true);
        }

        var afterContent = await project.ReadSourceAsync("Service.cs");
        Assert.Equal(originalContent, afterContent);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Error: method not found
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Throws_an_error_when_the_method_is_missing()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Dummy.cs", """
            namespace TestApp;

            public class Dummy
            {
                public void DoSomething(int x) { }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TransformParametersService.RunAsync(sln, "Dummy.NonExistentMethod", "Args", null, dryRun: false));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Error: method has no parameters
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Throws_an_error_when_the_method_has_no_parameters()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Empty.cs", """
            namespace TestApp;

            public class Empty
            {
                public void Run() { }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TransformParametersService.RunAsync(sln, "Empty.Run", "RunArgs", null, dryRun: false));

            Assert.Contains("no parameters", ex.Message);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Error: invalid method spec format
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Throws_an_error_for_an_invalid_method_specification()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Stub.cs", """
            namespace TestApp;

            public class Stub { }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TransformParametersService.RunAsync(sln, "NoMethodSeparatorHere", "Args", null, dryRun: false));

            Assert.Contains("TypeName.MethodName", ex.Message);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-012: --existing-type with matching record → positional call-site rewrite
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rewrites_call_sites_with_positional_construction()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("OrderArgs.cs", """
            namespace TestApp;

            public record OrderArgs(string CustomerId, string ProductId, int Quantity);
            """);
        project.AddSource("OrderService.cs", """
            namespace TestApp;

            public class OrderService
            {
                public void PlaceOrder(string customerId, string productId, int quantity)
                {
                    _ = customerId + productId + quantity;
                }
            }
            """);
        project.AddSource("Caller.cs", """
            namespace TestApp;

            public class Caller
            {
                public void Run()
                {
                    var svc = new OrderService();
                    svc.PlaceOrder("c1", "p1", 3);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            await TransformParametersService.RunAsync(sln, "OrderService.PlaceOrder", null, "TestApp.OrderArgs", dryRun: false);
        }

        var serviceContent = await project.ReadSourceAsync("OrderService.cs");
        var callerContent = await project.ReadSourceAsync("Caller.cs");

        // Method signature rewritten to use existing type
        Assert.Contains("PlaceOrder(OrderArgs args)", serviceContent);
        Assert.DoesNotContain("string customerId", serviceContent);

        // Call site uses positional constructor (record)
        Assert.Contains("PlaceOrder(new OrderArgs(\"c1\", \"p1\", 3))", callerContent);
        Assert.DoesNotContain("PlaceOrder(\"c1\", \"p1\", 3)", callerContent);

        // No new record generated in OrderService.cs
        Assert.DoesNotContain("record OrderArgs", serviceContent);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-012: --existing-type with matching class → object-initializer call-site rewrite
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rewrites_call_sites_with_object_initializers()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("SearchArgs.cs", """
            namespace TestApp;

            public class SearchArgs
            {
                public string Query { get; set; } = string.Empty;
                public int MaxResults { get; set; }
            }
            """);
        project.AddSource("SearchService.cs", """
            namespace TestApp;

            public class SearchService
            {
                public void Search(string query, int maxResults)
                {
                    _ = query + maxResults;
                }
            }
            """);
        project.AddSource("SearchCaller.cs", """
            namespace TestApp;

            public class SearchCaller
            {
                public void Run()
                {
                    var svc = new SearchService();
                    svc.Search("hello", 10);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            await TransformParametersService.RunAsync(sln, "SearchService.Search", null, "TestApp.SearchArgs", dryRun: false);
        }

        var serviceContent = await project.ReadSourceAsync("SearchService.cs");
        var callerContent = await project.ReadSourceAsync("SearchCaller.cs");

        // Method signature rewritten
        Assert.Contains("Search(SearchArgs args)", serviceContent);

        // Call site uses object initializer (class)
        Assert.Contains("Search(new SearchArgs", callerContent);
        Assert.Contains("Query = \"hello\"", callerContent);
        Assert.Contains("MaxResults = 10", callerContent);
        Assert.DoesNotContain("Search(\"hello\", 10)", callerContent);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-012: Missing property → error listing missing names
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Throws_an_error_listing_missing_properties()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("IncompleteArgs.cs", """
            namespace TestApp;

            public class IncompleteArgs
            {
                public string Name { get; set; } = string.Empty;
                // Missing: Age, Email
            }
            """);
        project.AddSource("PersonService.cs", """
            namespace TestApp;

            public class PersonService
            {
                public void Register(string name, int age, string email)
                {
                    _ = name + age + email;
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TransformParametersService.RunAsync(sln, "PersonService.Register", null, "TestApp.IncompleteArgs", dryRun: false));

            Assert.Contains("age", ex.Message);
            Assert.Contains("email", ex.Message);
            Assert.Contains("missing", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-012: Partial match → error
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Lists_only_the_missing_properties_in_the_error()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("PartialArgs.cs", """
            namespace TestApp;

            public record PartialArgs(string X);
            """);
        project.AddSource("MathService.cs", """
            namespace TestApp;

            public class MathService
            {
                public int Add(string x, string y) => x.Length + y.Length;
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TransformParametersService.RunAsync(sln, "MathService.Add", null, "TestApp.PartialArgs", dryRun: false));

            // 'y' is missing; 'x' is present
            Assert.Contains("y", ex.Message);
            Assert.DoesNotContain("\"x\"", ex.Message);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-012: Both --new-type and --existing-type → error
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Throws_a_mutual_exclusion_error()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("BothFlagsService.cs", """
            namespace TestApp;

            public class BothFlagsService
            {
                public void Do(int x, int y) { }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TransformParametersService.RunAsync(sln, "BothFlagsService.Do", "NewArgs", "TestApp.ExistingArgs", dryRun: false));

            Assert.Contains("mutually exclusive", ex.Message);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-012: Neither flag → error
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Throws_an_error_when_no_target_type_is_specified()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("NeitherService.cs", """
            namespace TestApp;

            public class NeitherService
            {
                public void Do(int x, int y) { }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TransformParametersService.RunAsync(sln, "NeitherService.Do", null, null, dryRun: false));

            Assert.Contains("--new-type", ex.Message);
            Assert.Contains("--existing-type", ex.Message);
        }
    }
}
