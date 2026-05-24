using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services.Suggest;
using Spectre.Console;

namespace RoslynTools.Cli.Suggest;

/// <summary>
///     Meta command that ranks the rt catalog against a natural-language query.
///     Thin adapter: parses CLI args into a <see cref="SuggestRequest" />, calls
///     <see cref="SuggestService.Suggest" />, formats the response. All logic
///     lives in the Core service so the MCP handler can reuse it.
/// </summary>
/// <remarks>
///     Not an <see cref="IRtCommand" /> — registered at root next to
///     <c>dump-help</c>. Sits outside the inspect/analyze/refactor groups because
///     it queries the catalog itself, not the user's solution.
/// </remarks>
internal static class SuggestCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static Command Build(IServiceProvider services)
    {
        var queryArg = new Argument<string>("query",
            "Natural-language description of what you want to do");
        var topOpt = new Option<int>("--top", () => 3,
            "Maximum number of ranked candidates to return");

        var cmd = new Command("suggest",
            "Find the right rt command by describing what you want to do")
        {
            queryArg, topOpt
        };

        cmd.SetHandler(context =>
        {
            var query = context.ParseResult.GetValueForArgument(queryArg);
            var top = context.ParseResult.GetValueForOption(topOpt);

            var suggest = services.GetRequiredService<SuggestService>();
            var response = suggest.Suggest(new SuggestRequest(query, top));

            Render(response);
        });

        return cmd;
    }

    private static void Render(SuggestResponse response)
    {
        if (RtContext.Format == OutputFormat.Json)
        {
            var json = JsonSerializer.Serialize(response, JsonOptions);
            RtConsole.WriteLine(RtOutputContext.Result, json);
            return;
        }

        if (response.Results.Count == 0)
        {
            RtConsole.WriteLine(RtOutputContext.Result, "No matching commands. Try different keywords.");
            return;
        }

        for (var i = 0; i < response.Results.Count; i++)
        {
            var hit = response.Results[i];
            var name = Markup.Escape(hit.Name);
            var category = Markup.Escape(hit.Category);
            RtConsole.WriteLine(RtOutputContext.Ui,
                $"[darkcyan]#{i + 1}[/]  [white]{name}[/]  [grey](score {hit.Score:F2})  [[{category}]][/]");
            if (!string.IsNullOrWhiteSpace(hit.WhenToUse))
                RtConsole.WriteLine(RtOutputContext.Ui,
                    $"   [grey]{Markup.Escape(Truncate(hit.WhenToUse, 200))}[/]");
            if (!string.IsNullOrWhiteSpace(hit.PrimaryExample))
                RtConsole.WriteLine(RtOutputContext.Ui,
                    $"   {Markup.Escape(hit.PrimaryExample)}");
            RtConsole.WriteLine(RtOutputContext.Result, "");
        }

        if (response.Related.Count > 0)
        {
            RtConsole.WriteLine(RtOutputContext.Ui, "[silver]Related:[/]");
            foreach (var rel in response.Related)
                RtConsole.WriteLine(RtOutputContext.Ui,
                    $"  [cyan]{Markup.Escape(rel.Name)}[/]  — [grey]{Markup.Escape(Truncate(rel.WhenToUse, 120))}[/]");
        }
    }

    private static string Truncate(string text, int max)
    {
        var firstLine = text.Split('\n', 2)[0].Trim();
        return firstLine.Length <= max ? firstLine : firstLine[..(max - 1)] + "…";
    }
}
