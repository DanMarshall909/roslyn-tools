namespace RoslynTools.Tests.Refactor;

public class AddTest
{
    [Fact(Skip = "rt add-test command not yet implemented")]
    [Trait("Concern", "MethodNameGeneration")]
    public void scenario_and_outcome_generates_method_name()
    {
        // `rt refactor add-test` should convert:
        //   --scenario "source file as first occurrence"
        //   --outcome "updates call site"
        // to method name: "with_source_file_as_first_occurrence_updates_call_site"
        //
        // When implemented, AddTestCommand should have this logic:
        // var scenario = "source file as first occurrence";
        // var outcome = "updates call site";
        // var methodName = "with_" + ToSnakeCase(scenario) + "_" + ToSnakeCase(outcome);
        // Assert.Equal("with_source_file_as_first_occurrence_updates_call_site", methodName);
    }

    [Fact(Skip = "rt add-test command not yet implemented")]
    [Trait("Concern", "FileGeneration")]
    public void command_name_determines_directory_structure()
    {
        // `rt refactor add-test --command ExtractMethod` should create/modify:
        //   tests/RoslynTools.Tests/Refactor/ExtractMethod.cs
        //
        // Command to category mapping:
        // - Any "inspect" command → tests/RoslynTools.Tests/Inspect/
        // - Any "analyze" command → tests/RoslynTools.Tests/Analyze/
        // - Any "refactor" command → tests/RoslynTools.Tests/Refactor/
    }

    [Fact(Skip = "rt add-test command not yet implemented")]
    [Trait("Concern", "FileGeneration")]
    public void existing_class_adds_method_to_file()
    {
        // If tests/RoslynTools.Tests/Refactor/ExtractMethod.cs exists,
        // add the new test method to the existing class.
        // If it doesn't exist, create it with a new class definition.
    }

    [Fact(Skip = "rt add-test command not yet implemented")]
    [Trait("Concern", "TraitGeneration")]
    public void concern_flag_adds_trait_attribute()
    {
        // `--concern SourceFileCallSite` should add:
        //   [Trait("Concern", "SourceFileCallSite")]
        // before the test method.
    }
}
