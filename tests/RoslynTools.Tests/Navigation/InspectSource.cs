using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Tests.Navigation;

public class InspectSource
{
    static InspectSource()
    {
        MsBuildFixture.Ensure();
    }

    [Fact]
    public async Task returns_source_text_file_and_line_range_for_method()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Calc.cs", """
                                     namespace TestApp;
                                     public class Calc
                                     {
                                         public int Add(int a, int b) => a + b;
                                     }
                                     """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.Calc.Add", "method");
            var result = await new InspectService().GetSourceAsync(sym);

            var entry = Assert.Single(result);
            Assert.Equal("TestApp.Calc.Add(int, int)", (string)entry["fqn"]!);
            Assert.NotNull(entry["file"]);
            Assert.NotNull(entry["lineFrom"]);
            Assert.NotNull(entry["lineTo"]);
            Assert.Contains("a + b", (string)entry["source"]!);
        }
    }

    [Fact]
    public async Task source_includes_xml_doc_comment()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Greeter.cs", """
                                        namespace TestApp;
                                        public class Greeter
                                        {
                                            /// <summary>Returns a greeting.</summary>
                                            public string Hello() => "hi";
                                        }
                                        """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.Greeter.Hello", "method");
            var result = await new InspectService().GetSourceAsync(sym);

            var entry = Assert.Single(result);
            Assert.Contains("Returns a greeting", (string)entry["source"]!);
        }
    }

    [Fact]
    public async Task line_range_is_one_based_and_spans_full_method()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Multi.cs", """
                                      namespace TestApp;
                                      public class Multi
                                      {
                                          public int Compute(int x)
                                          {
                                              var y = x * 2;
                                              return y + 1;
                                          }
                                      }
                                      """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.Multi.Compute", "method");
            var result = await new InspectService().GetSourceAsync(sym);

            var entry = Assert.Single(result);
            var lineFrom = (int)entry["lineFrom"]!;
            var lineTo = (int)entry["lineTo"]!;
            Assert.True(lineFrom >= 1, "lineFrom must be 1-based");
            Assert.True(lineTo > lineFrom, "multi-line method must have lineTo > lineFrom");
        }
    }
}
