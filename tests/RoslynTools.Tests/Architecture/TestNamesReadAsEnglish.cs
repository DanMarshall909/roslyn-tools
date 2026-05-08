namespace RoslynTools.Tests.Architecture;

public sealed class TestNamesReadAsEnglish
{
    [Fact(Skip = "Future feature: wire a lightweight grammar checker into the test-name architecture gate.")]
    public void language_tool_flags_test_names_that_are_not_plain_english_sentences()
    {
        /*
         * Intended implementation:
         * 1. Enumerate [Fact] and [Theory] methods in tests.
         * 2. Build the candidate sentence from the containing class name plus method name.
         * 3. Convert PascalCase and snake_case to plain words.
         * 4. Send each sentence to a local LanguageTool-compatible checker.
         * 5. Fail with actionable file/line/name diagnostics for grammar findings that are not allowlisted.
         *
         * The normal test suite should stay deterministic. If LanguageTool is external, run this only when
         * the checker is explicitly available or provide a repo-local deterministic wrapper first.
         */
        throw new NotImplementedException();
    }
}
