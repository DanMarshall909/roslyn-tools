using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class TransformParameters
{
    private static string FilePath(AdhocProjectBuilder project, string relativePath)
    {
        return Path.Combine(project.ProjectDir, relativePath);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Simple 3-param method: record generated, signature rewritten, call site updated
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task generates_a_record_and_rewrites_the_signature_and_call_site()
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
            await new TransformParametersService().RunAsync(sln, "OrderService.PlaceOrder", "PlaceOrderArgs", null,
                false);
        }

        var serviceContent = await project.ReadSourceAsync("OrderService.cs");
        var callerContent = await project.ReadSourceAsync("Caller.cs");

        // Record generated in the same file
        serviceContent.ShouldContain("record PlaceOrderArgs", Case.Sensitive);
        serviceContent.ShouldContain("string CustomerId", Case.Sensitive);
        serviceContent.ShouldContain("string ProductId", Case.Sensitive);
        serviceContent.ShouldContain("int Quantity", Case.Sensitive);

        // Method signature rewritten
        serviceContent.ShouldContain("PlaceOrder(PlaceOrderArgs args)", Case.Sensitive);
        serviceContent.ShouldNotContain("PlaceOrder(string customerId, string productId, int quantity)", Case.Sensitive);

        // Call site updated
        callerContent.ShouldContain("PlaceOrder(new PlaceOrderArgs(\"c1\", \"p1\", 3))", Case.Sensitive);
        callerContent.ShouldNotContain("PlaceOrder(\"c1\", \"p1\", 3)", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Default value preserved in record parameter list
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task preserves_default_values_in_the_generated_record()
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
            await new TransformParametersService().RunAsync(sln, "Greeter.Greet", "GreetArgs", null, false);
        }

        var content = await project.ReadSourceAsync("Greeter.cs");

        content.ShouldContain("record GreetArgs", Case.Sensitive);
        // Default value 1 must appear in the record parameter list
        content.ShouldContain("Times = 1", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  out/ref/in parameters → error, skipped params listed
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task throws_a_clear_error_for_out_parameters()
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
                new TransformParametersService().RunAsync(sln, "Parser.TryParse", "ParseArgs", null, false));

            ex.Message.ShouldContain("result", Case.Sensitive);
            ex.Message.ShouldContain("out/ref/in", Case.Sensitive);
        }
    }

    [Fact]
    public async Task throws_a_clear_error_for_ref_parameters()
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
                new TransformParametersService().RunAsync(sln, "Modifier.Increment", "IncrementArgs", null, false));

            ex.Message.ShouldContain("counter", Case.Sensitive);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Multi-call-site update across multiple files
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task updates_every_call_site()
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
            await new TransformParametersService().RunAsync(sln, "Calculator.Add", "AddArgs", null, false);
        }

        var calcContent = await project.ReadSourceAsync("Calculator.cs");
        var client1Content = await project.ReadSourceAsync("Client1.cs");
        var client2Content = await project.ReadSourceAsync("Client2.cs");

        calcContent.ShouldContain("record AddArgs", Case.Sensitive);
        calcContent.ShouldContain("Add(AddArgs args)", Case.Sensitive);

        client1Content.ShouldContain("Add(new AddArgs(1, 2))", Case.Sensitive);
        client1Content.ShouldContain("Add(new AddArgs(3, 4))", Case.Sensitive);
        client1Content.ShouldNotContain("Add(1, 2)", Case.Sensitive);

        client2Content.ShouldContain("Add(new AddArgs(10, 20))", Case.Sensitive);
        client2Content.ShouldNotContain("Add(10, 20)", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Dry-run emits diff without writing
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task does_not_modify_files()
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
            await new TransformParametersService().RunAsync(sln, "Service.Execute", "ExecuteArgs", null, true);
        }

        var afterContent = await project.ReadSourceAsync("Service.cs");
        afterContent.ShouldBe(originalContent);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Error: method not found
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task throws_an_error_when_the_method_is_missing()
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
                new TransformParametersService().RunAsync(sln, "Dummy.NonExistentMethod", "Args", null, false));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Error: method has no parameters
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task throws_an_error_when_the_method_has_no_parameters()
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
                new TransformParametersService().RunAsync(sln, "Empty.Run", "RunArgs", null, false));

            ex.Message.ShouldContain("no parameters", Case.Sensitive);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Error: invalid method spec format
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task throws_an_error_for_an_invalid_method_specification()
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
                new TransformParametersService().RunAsync(sln, "NoMethodSeparatorHere", "Args", null, false));

            ex.Message.ShouldContain("TypeName.MethodName", Case.Sensitive);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-012: --existing-type with matching record → positional call-site rewrite
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task rewrites_call_sites_with_positional_construction()
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
            await new TransformParametersService().RunAsync(sln, "OrderService.PlaceOrder", null, "TestApp.OrderArgs",
                false);
        }

        var serviceContent = await project.ReadSourceAsync("OrderService.cs");
        var callerContent = await project.ReadSourceAsync("Caller.cs");

        // Method signature rewritten to use existing type
        serviceContent.ShouldContain("PlaceOrder(OrderArgs args)", Case.Sensitive);
        serviceContent.ShouldNotContain("string customerId", Case.Sensitive);

        // Call site uses positional constructor (record)
        callerContent.ShouldContain("PlaceOrder(new OrderArgs(\"c1\", \"p1\", 3))", Case.Sensitive);
        callerContent.ShouldNotContain("PlaceOrder(\"c1\", \"p1\", 3)", Case.Sensitive);

        // No new record generated in OrderService.cs
        serviceContent.ShouldNotContain("record OrderArgs", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-012: --existing-type with matching class → object-initializer call-site rewrite
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task rewrites_call_sites_with_object_initializers()
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
            await new TransformParametersService().RunAsync(sln, "SearchService.Search", null, "TestApp.SearchArgs",
                false);
        }

        var serviceContent = await project.ReadSourceAsync("SearchService.cs");
        var callerContent = await project.ReadSourceAsync("SearchCaller.cs");

        // Method signature rewritten
        serviceContent.ShouldContain("Search(SearchArgs args)", Case.Sensitive);

        // Call site uses object initializer (class)
        callerContent.ShouldContain("Search(new SearchArgs", Case.Sensitive);
        callerContent.ShouldContain("Query = \"hello\"", Case.Sensitive);
        callerContent.ShouldContain("MaxResults = 10", Case.Sensitive);
        callerContent.ShouldNotContain("Search(\"hello\", 10)", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-012: Missing property → error listing missing names
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task throws_an_error_listing_missing_properties()
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
                new TransformParametersService().RunAsync(sln, "PersonService.Register", null, "TestApp.IncompleteArgs",
                    false));

            ex.Message.ShouldContain("age", Case.Sensitive);
            ex.Message.ShouldContain("email", Case.Sensitive);
            Assert.Contains("missing", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-012: Partial match → error
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task lists_only_the_missing_properties_in_the_error()
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
                new TransformParametersService().RunAsync(sln, "MathService.Add", null, "TestApp.PartialArgs", false));

            // 'y' is missing; 'x' is present
            ex.Message.ShouldContain("y", Case.Sensitive);
            ex.Message.ShouldNotContain("\"x\"", Case.Sensitive);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-012: Both --new-type and --existing-type → error
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task throws_a_mutual_exclusion_error()
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
                new TransformParametersService().RunAsync(sln, "BothFlagsService.Do", "NewArgs", "TestApp.ExistingArgs",
                    false));

            ex.Message.ShouldContain("mutually exclusive", Case.Sensitive);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-012: Neither flag → error
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task throws_an_error_when_no_target_type_is_specified()
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
                new TransformParametersService().RunAsync(sln, "NeitherService.Do", null, null, false));

            ex.Message.ShouldContain("--new-type", Case.Sensitive);
            ex.Message.ShouldContain("--existing-type", Case.Sensitive);
        }
    }
}
