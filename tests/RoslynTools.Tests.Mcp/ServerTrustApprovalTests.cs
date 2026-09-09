using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Mcp;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mcp;

public sealed class ServerTrustApprovalTests
{
    [Theory]
    [InlineData("2025-11-25", "{\"elicitation\":{\"form\":{}}}")]
    [InlineData("2025-06-18", "{\"elicitation\":{}}")]
    public async Task explicit_client_approval_saves_only_the_displayed_root(string version, string capabilities)
    {
        await using var session = new Session();
        await session.InitializeAsync(version, capabilities);

        var prompt = await session.RequestTrustAsync();
        prompt.GetProperty("method").GetString().ShouldBe("elicitation/create");
        var parameters = prompt.GetProperty("params");
        var message = parameters.GetProperty("message").GetString().ShouldNotBeNull();
        message.ShouldContain(session.Root);
        message.ShouldContain("execute repository-controlled");
        parameters.GetProperty("requestedSchema").GetProperty("properties")
            .GetProperty("confirm").GetProperty("default").GetBoolean().ShouldBeFalse();
        session.Store.Read().ShouldBeEmpty();

        session.Respond(prompt, "accept", new { confirm = true });
        var result = await session.ReadOutputAsync();

        result.GetProperty("id").GetInt32().ShouldBe(2);
        result.GetProperty("result").GetProperty("isError").GetBoolean().ShouldBeFalse();
        session.Store.Read().ShouldBe([session.Root]);
    }

    [Theory]
    [InlineData("decline", "{\"confirm\":true}")]
    [InlineData("cancel", "{\"confirm\":true}")]
    [InlineData("accept", "{\"confirm\":false}")]
    [InlineData("accept", "{\"confirm\":\"true\"}")]
    [InlineData("accept", "{}")]
    [InlineData("accept", "null")]
    public async Task declined_canceled_or_malformed_confirmation_does_not_save_trust(string action, string content)
    {
        await using var session = new Session();
        await session.InitializeAsync();
        var prompt = await session.RequestTrustAsync();

        session.Respond(prompt, action, JsonSerializer.Deserialize<JsonElement>(content));

        var result = await session.ReadOutputAsync();
        result.GetProperty("result").GetProperty("isError").GetBoolean().ShouldBeTrue();
        session.Store.Read().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("2024-11-05", "{\"elicitation\":{}}", true)]
    [InlineData("2025-11-25", "{}", true)]
    [InlineData("2025-11-25", "{\"elicitation\":{\"url\":{}}}", true)]
    [InlineData("2025-11-25", "{\"elicitation\":{\"form\":true}}", true)]
    [InlineData("2025-11-25", "{\"elicitation\":{}}", false)]
    public async Task missing_protocol_capability_or_initialized_notification_denies_without_prompt(
        string version, string capabilities, bool initialized)
    {
        await using var session = new Session();
        await session.InitializeAsync(version, capabilities, initialized);

        var result = await session.RequestTrustAsync(forgedApproval: true);

        result.TryGetProperty("method", out _).ShouldBeFalse();
        result.GetProperty("result").GetProperty("isError").GetBoolean().ShouldBeTrue();
        session.Store.Read().ShouldBeEmpty();
    }

    [Fact]
    public async Task tool_arguments_cannot_replace_the_separate_client_confirmation()
    {
        await using var session = new Session();
        await session.InitializeAsync();
        var prompt = await session.RequestTrustAsync(forgedApproval: true);

        session.Store.Read().ShouldBeEmpty();
        session.Respond(prompt, "decline", new { confirm = true });

        (await session.ReadOutputAsync()).GetProperty("result").GetProperty("isError").GetBoolean().ShouldBeTrue();
        session.Store.Read().ShouldBeEmpty();
    }

    [Fact]
    public async Task mismatched_responses_cannot_approve_and_ping_is_answered_while_waiting()
    {
        await using var session = new Session();
        await session.InitializeAsync();
        var prompt = await session.RequestTrustAsync();
        session.Send(new { jsonrpc = "2.0", id = "wrong-id", result = new { action = "accept", content = new { confirm = true } } });
        session.Send(new { jsonrpc = "2.0", id = 17, method = "ping" });

        var ping = await session.ReadOutputAsync();
        ping.GetProperty("id").GetInt32().ShouldBe(17);
        ping.TryGetProperty("result", out _).ShouldBeTrue();
        session.Store.Read().ShouldBeEmpty();

        session.Respond(prompt, "decline", new { confirm = false });
        (await session.ReadOutputAsync()).GetProperty("result").GetProperty("isError").GetBoolean().ShouldBeTrue();
        session.Store.Read().ShouldBeEmpty();
    }

    [Fact]
    public async Task error_response_denies_even_when_it_also_contains_an_accept_result()
    {
        await using var session = new Session();
        await session.InitializeAsync();
        var prompt = await session.RequestTrustAsync();

        session.Send(new
        {
            jsonrpc = "2.0",
            id = prompt.GetProperty("id").GetString(),
            error = new { code = -32603, message = "client failure" },
            result = new { action = "accept", content = new { confirm = true } }
        });

        (await session.ReadOutputAsync()).GetProperty("result").GetProperty("isError").GetBoolean().ShouldBeTrue();
        session.Store.Read().ShouldBeEmpty();
    }

    [Fact]
    public async Task concurrent_tool_call_is_rejected_without_consuming_the_pending_approval()
    {
        await using var session = new Session();
        await session.InitializeAsync();
        var prompt = await session.RequestTrustAsync();
        session.SendToolCall(3);

        var busy = await session.ReadOutputAsync();
        busy.GetProperty("id").GetInt32().ShouldBe(3);
        busy.GetProperty("error").GetProperty("code").GetInt32().ShouldBe(-32000);
        session.Store.Read().ShouldBeEmpty();

        session.Respond(prompt, "accept", new { confirm = true });
        var accepted = await session.ReadOutputAsync();
        accepted.GetProperty("id").GetInt32().ShouldBe(2);
        accepted.GetProperty("result").GetProperty("isError").GetBoolean().ShouldBeFalse();
        session.Store.Read().ShouldBe([session.Root]);
    }

    [Fact]
    public async Task each_request_gets_a_new_confirmation_and_old_responses_cannot_be_replayed()
    {
        await using var session = new Session();
        await session.InitializeAsync();
        var first = await session.RequestTrustAsync();
        session.Respond(first, "decline", new { confirm = false });
        await session.ReadOutputAsync();
        var second = await session.RequestTrustAsync(3);
        second.GetProperty("id").GetString().ShouldNotBe(first.GetProperty("id").GetString());

        session.Respond(first, "accept", new { confirm = true });
        session.Send(new { jsonrpc = "2.0", id = 18, method = "ping" });
        (await session.ReadOutputAsync()).GetProperty("id").GetInt32().ShouldBe(18);
        session.Store.Read().ShouldBeEmpty();

        session.Respond(second, "decline", new { confirm = false });
        (await session.ReadOutputAsync()).GetProperty("result").GetProperty("isError").GetBoolean().ShouldBeTrue();
        session.Store.Read().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task cancellation_of_the_originating_call_or_elicitation_prevents_persistence(bool cancelElicitation)
    {
        await using var session = new Session();
        await session.InitializeAsync();
        var prompt = await session.RequestTrustAsync();
        session.Send(new
        {
            jsonrpc = "2.0",
            method = "notifications/cancelled",
            @params = new { requestId = cancelElicitation ? (object)prompt.GetProperty("id").GetString()! : 2 }
        });
        session.Respond(prompt, "accept", new { confirm = true });

        var cancellation = await session.ReadOutputAsync();
        cancellation.GetProperty("method").GetString().ShouldBe("notifications/cancelled");
        cancellation.GetProperty("params").GetProperty("requestId").GetString()
            .ShouldBe(prompt.GetProperty("id").GetString());
        var denied = await session.ReadOutputAsync();
        denied.GetProperty("id").GetInt32().ShouldBe(2);
        session.Send(new { jsonrpc = "2.0", id = 19, method = "ping" });
        (await session.ReadOutputAsync()).GetProperty("id").GetInt32().ShouldBe(19);
        session.Store.Read().ShouldBeEmpty();
    }

    [Fact]
    public async Task end_of_input_denies_pending_approval()
    {
        await using var session = new Session();
        await session.InitializeAsync();
        await session.RequestTrustAsync();

        session.CompleteInput();
        (await session.ReadOutputAsync()).GetProperty("result").GetProperty("isError").GetBoolean().ShouldBeTrue();
        await session.RunTask.WaitAsync(TimeSpan.FromSeconds(10));
        session.Store.Read().ShouldBeEmpty();
    }

    [Fact]
    public async Task timeout_denies_and_preserves_the_next_input_for_the_main_loop()
    {
        await using var session = new Session(TimeSpan.FromMilliseconds(150));
        await session.InitializeAsync();
        var prompt = await session.RequestTrustAsync();

        (await session.ReadOutputAsync()).GetProperty("method").GetString().ShouldBe("notifications/cancelled");
        (await session.ReadOutputAsync()).GetProperty("result").GetProperty("isError").GetBoolean().ShouldBeTrue();
        session.Respond(prompt, "accept", new { confirm = true });
        session.Send(new { jsonrpc = "2.0", id = 20, method = "ping" });

        (await session.ReadOutputAsync()).GetProperty("id").GetInt32().ShouldBe(20);
        session.Store.Read().ShouldBeEmpty();
    }

    [Fact]
    public async Task approval_service_denies_outside_an_originating_tool_request()
    {
        var approval = new McpRepositoryTrustApproval();

        (await approval.ApproveAsync(Path.GetTempPath())).ShouldBeFalse();
    }

    [Fact]
    public async Task disposed_request_scope_revokes_authority_in_inherited_execution_contexts()
    {
        var approval = new McpRepositoryTrustApproval();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> inheritedRequest;
        using (approval.EnterRequestScope((_, _) => Task.FromResult(true)))
        {
            inheritedRequest = Task.Run(async () =>
            {
                await release.Task;
                return await approval.ApproveAsync(Path.GetTempPath());
            });
        }

        release.SetResult();

        (await inheritedRequest).ShouldBeFalse();
    }

    [Fact]
    public async Task trust_administration_is_discoverable_without_workspace_authority()
    {
        await using var session = new Session();
        session.Send(new { jsonrpc = "2.0", id = 4, method = "tools/list" });
        var names = (await session.ReadOutputAsync()).GetProperty("result").GetProperty("tools")
            .EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ToArray();

        names.ShouldContain("trust_root");
        names.ShouldContain("list_trusted_roots");
        names.ShouldContain("untrust_root");
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"rt-elicitation-{Guid.NewGuid():N}");
        private readonly InputReader _input = new();
        private readonly OutputWriter _output = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly ServiceProvider _provider;

        internal Session(TimeSpan? approvalTimeout = null)
        {
            Root = Path.Combine(_directory, "repo");
            Directory.CreateDirectory(Root);
            Store = new RepositoryTrustStore(Path.Combine(_directory, "config", "trust.json"));
            var services = new ServiceCollection();
            RoslynTools.Bootstrap.RegisterServices(services);
            services.AddSingleton(Store);
            services.AddMcpServices(_input, _output);
            services.AddSingleton(new McpRepositoryTrustApproval(approvalTimeout));
            _provider = services.BuildServiceProvider();
            RunTask = _provider.GetRequiredService<Server>().RunAsync(_stop.Token);
        }

        internal string Root { get; }
        internal RepositoryTrustStore Store { get; }
        internal Task RunTask { get; }

        internal async Task InitializeAsync(string version = "2025-11-25",
            string capabilities = "{\"elicitation\":{\"form\":{}}}", bool initialized = true)
        {
            Send(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new { protocolVersion = version, capabilities = JsonSerializer.Deserialize<JsonElement>(capabilities) }
            });
            var result = await ReadOutputAsync();
            result.GetProperty("result").GetProperty("protocolVersion").GetString().ShouldBe(version);
            if (initialized)
                Send(new { jsonrpc = "2.0", method = "notifications/initialized" });
        }

        internal async Task<JsonElement> RequestTrustAsync(int id = 2, bool forgedApproval = false)
        {
            SendToolCall(id, forgedApproval);
            return await ReadOutputAsync();
        }

        internal void SendToolCall(int id, bool forgedApproval = false)
        {
            var arguments = new Dictionary<string, object> { ["root"] = Root, ["dry-run"] = false };
            if (forgedApproval)
            {
                arguments["confirm"] = true;
                arguments["trustRepository"] = true;
                arguments["approved"] = true;
            }
            Send(new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name = "trust_root", arguments } });
        }

        internal void Respond(JsonElement prompt, string action, object? content)
            => Send(new { jsonrpc = "2.0", id = prompt.GetProperty("id").GetString(), result = new { action, content } });

        internal void Send(object message) => _input.Lines.Writer.TryWrite(JsonSerializer.Serialize(message)).ShouldBeTrue();

        internal async Task<JsonElement> ReadOutputAsync()
        {
            var line = await _output.Lines.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            return JsonSerializer.Deserialize<JsonElement>(line);
        }

        internal void CompleteInput() => _input.Lines.Writer.TryComplete();

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            CompleteInput();
            await RunTask.WaitAsync(TimeSpan.FromSeconds(10));
            _provider.Dispose();
            _stop.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class InputReader : TextReader
    {
        internal Channel<string> Lines { get; } = Channel.CreateUnbounded<string>();

        public override async Task<string?> ReadLineAsync()
        {
            try
            {
                return await Lines.Reader.ReadAsync();
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }
    }

    private sealed class OutputWriter : TextWriter
    {
        internal Channel<string> Lines { get; } = Channel.CreateUnbounded<string>();
        public override Encoding Encoding => Encoding.UTF8;

        public override Task WriteLineAsync(string? value)
        {
            Lines.Writer.TryWrite(value ?? string.Empty).ShouldBeTrue();
            return Task.CompletedTask;
        }
    }
}
