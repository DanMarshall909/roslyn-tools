using RoslynTools.Core;

namespace RoslynTools.Tests;

[Collection("MSBuild")]
public class TransformParametersTests
{
    private static string FilePath(TestProjectBuilder project, string relativePath)
        => Path.Combine(project.ProjectDir, relativePath);

    // ──────────────────────────────────────────────────────────────────────────
    //  Simple 3-param method: record generated, signature rewritten, call site updated
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Simple3ParamMethod_GeneratesRecord_RewritesSignatureAndCallSite()
    {
        using var project = new TestProjectBuilder();
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

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            await TransformParametersCore.RunAsync(sln, "OrderService.PlaceOrder", "PlaceOrderArgs", dryRun: false);
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
    public async Task DefaultValue_PreservedInRecord()
    {
        using var project = new TestProjectBuilder();
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

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            await TransformParametersCore.RunAsync(sln, "Greeter.Greet", "GreetArgs", dryRun: false);
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
    public async Task OutParam_ThrowsWithClearMessage()
    {
        using var project = new TestProjectBuilder();
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

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TransformParametersCore.RunAsync(sln, "Parser.TryParse", "ParseArgs", dryRun: false));

            Assert.Contains("result", ex.Message);
            Assert.Contains("out/ref/in", ex.Message);
        }
    }

    [Fact]
    public async Task RefParam_ThrowsWithClearMessage()
    {
        using var project = new TestProjectBuilder();
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

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TransformParametersCore.RunAsync(sln, "Modifier.Increment", "IncrementArgs", dryRun: false));

            Assert.Contains("counter", ex.Message);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Multi-call-site update across multiple files
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultipleCallSites_AllUpdated()
    {
        using var project = new TestProjectBuilder();
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

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            await TransformParametersCore.RunAsync(sln, "Calculator.Add", "AddArgs", dryRun: false);
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
    public async Task DryRun_DoesNotModifyFiles()
    {
        using var project = new TestProjectBuilder();
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

        var slnPath = await project.BuildAsync();
        var originalContent = await project.ReadSourceAsync("Service.cs");

        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            await TransformParametersCore.RunAsync(sln, "Service.Execute", "ExecuteArgs", dryRun: true);
        }

        var afterContent = await project.ReadSourceAsync("Service.cs");
        Assert.Equal(originalContent, afterContent);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Error: method not found
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MethodNotFound_Throws()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Dummy.cs", """
            namespace TestApp;

            public class Dummy
            {
                public void DoSomething(int x) { }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TransformParametersCore.RunAsync(sln, "Dummy.NonExistentMethod", "Args", dryRun: false));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Error: method has no parameters
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoParameters_Throws()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Empty.cs", """
            namespace TestApp;

            public class Empty
            {
                public void Run() { }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TransformParametersCore.RunAsync(sln, "Empty.Run", "RunArgs", dryRun: false));

            Assert.Contains("no parameters", ex.Message);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Error: invalid method spec format
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvalidMethodSpec_Throws()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Stub.cs", """
            namespace TestApp;

            public class Stub { }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TransformParametersCore.RunAsync(sln, "NoMethodSeparatorHere", "Args", dryRun: false));

            Assert.Contains("TypeName.MethodName", ex.Message);
        }
    }
}
