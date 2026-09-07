using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Architecture;

public sealed class ApplicationCommandFoundationContracts
{
    private static readonly Assembly ApplicationAssembly = typeof(OutlineOperation).Assembly;

    [Fact]
    public void Application_ports_expose_definition_request_and_response_types()
    {
        var definitionType = RequiredType("RoslynTools.Core.ApplicationCommandDefinition");
        var properties = typeof(ICommandApplicationPort).GetProperties()
            .ToDictionary(property => property.Name, StringComparer.Ordinal);

        properties["Definition"].PropertyType.ShouldBe(definitionType);
        properties["RequestType"].PropertyType.ShouldBe(typeof(Type));
        properties["ResponseType"].PropertyType.ShouldBe(typeof(Type));
        properties.ShouldNotContainKey("CommandName");
    }

    [Fact]
    public void Typed_application_port_returns_result_with_a_structured_RtError()
    {
        var portType = typeof(CommandApplicationPort<,>);
        var execute = portType.GetMethod(
            "ExecuteAsync",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        execute.ShouldNotBeNull();
        var taskType = execute.ReturnType;
        taskType.GetGenericTypeDefinition().ShouldBe(typeof(Task<>));

        var resultType = taskType.GetGenericArguments().Single();
        resultType.GetGenericTypeDefinition().ShouldBe(typeof(Result<,>));
        resultType.GetGenericArguments()[0].ShouldBe(portType.GetGenericArguments()[1]);
        resultType.GetGenericArguments()[1].ShouldBe(typeof(RtError));

        typeof(CommandPortResult).GetProperty(nameof(CommandPortResult.Error))!
            .PropertyType.ShouldBe(typeof(RtError));
    }

    [Fact]
    public void Application_definition_owns_the_complete_host_neutral_contract()
    {
        var definitionType = RequiredType("RoslynTools.Core.ApplicationCommandDefinition");
        var propertyNames = definitionType.GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        string[] requiredProperties =
        [
            "Name",
            "Info",
            "RequestType",
            "ResponseType",
            "McpVisible",
            "Safety",
            "ExecutionPlacement",
            "SuggestedTools",
            "Product"
        ];
        foreach (var requiredProperty in requiredProperties)
            propertyNames.ShouldContain(requiredProperty);

        AssertHostNeutral(definitionType);
        AssertHostNeutral(RequiredType("RoslynTools.Core.CommandExecutionPlacement"));
        AssertHostNeutral(RequiredType("RoslynTools.Core.CommandPropertyDescriptionAttribute"));
    }

    [Fact]
    public void Execution_placement_has_the_two_final_policies()
    {
        var placementType = RequiredType("RoslynTools.Core.CommandExecutionPlacement");

        placementType.IsEnum.ShouldBeTrue();
        Enum.GetNames(placementType).ShouldBe(["DaemonPreferred", "InProcess"]);
    }

    [Fact]
    public void Pilot_operations_publish_the_final_definition_contract()
    {
        var definitionType = RequiredType("RoslynTools.Core.ApplicationCommandDefinition");

        AssertDefinitionProperty(typeof(OutlineOperation), definitionType);
        AssertDefinitionProperty(typeof(AddDependencyOperation), definitionType);
    }

    [Fact]
    public void Pilot_definitions_publish_final_safety_placement_and_transport_types()
    {
        var outline = OutlineCommandDefinition.Definition;
        outline.Name.ShouldBe("outline");
        outline.RequestType.ShouldBe(typeof(OutlineRequest));
        outline.ResponseType.ShouldBe(typeof(RtResultEnvelope<OutlineResult>));
        outline.McpVisible.ShouldBeTrue();
        outline.Safety.ShouldBe(CommandSafety.ReadOnly);
        outline.ExecutionPlacement.ShouldBe(CommandExecutionPlacement.DaemonPreferred);
        outline.SuggestedTools.Select(tool => tool.ToolName)
            .ShouldBe(["describe", "find_refs"]);
        outline.Product.Maturity.ShouldBe(CapabilityMaturity.StableCandidate);
        outline.Product.WorkspaceRequirement.ShouldBe(WorkspaceRequirement.Semantic);

        var addDependency = AddDependencyCommandDefinition.Definition;
        addDependency.Name.ShouldBe("add_dependency");
        addDependency.RequestType.ShouldBe(typeof(AddDependencyRequest));
        addDependency.ResponseType.ShouldBe(typeof(MutationExecutionResult<AddDependencyResult>));
        addDependency.McpVisible.ShouldBeTrue();
        addDependency.Safety.ShouldBe(CommandSafety.ReviewByDefault);
        addDependency.ExecutionPlacement.ShouldBe(CommandExecutionPlacement.InProcess);
        addDependency.SuggestedTools.Select(tool => tool.ToolName)
            .ShouldBe(["describe", "find_refs"]);
        addDependency.Product.Effects.ShouldBe(
            CommandEffects.RepositoryRead | CommandEffects.RepositoryWrite);
        addDependency.Product.ConditionallyMutable.ShouldBeTrue();
        addDependency.Product.Audience.ShouldBe(CommandAudience.Public);
        addDependency.Product.Limits.DefaultTimeoutMilliseconds.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void stable_candidate_operations_cannot_bypass_the_versioned_result_envelope()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        RoslynTools.Bootstrap.RegisterServices(services);
        using var provider = services.BuildServiceProvider();
        var stableCandidates = provider
            .GetServices<ICommandApplicationPort>()
            .Where(port => port is not ILegacyCommandApplicationPort
                           && port.Definition.Product.Maturity == CapabilityMaturity.StableCandidate)
            .ToArray();

        stableCandidates.ShouldNotBeEmpty();
        stableCandidates.ShouldAllBe(port =>
            port.ResponseType.IsGenericType
            && port.ResponseType.GetGenericTypeDefinition() == typeof(RtResultEnvelope<>));
    }

    private static Type RequiredType(string fullName) =>
        ApplicationAssembly.GetType(fullName).ShouldNotBeNull($"Missing host-neutral contract '{fullName}'.");

    private static void AssertDefinitionProperty(Type operationType, Type definitionType)
    {
        var definition = operationType.GetProperty("Definition", BindingFlags.Instance | BindingFlags.Public);

        definition.ShouldNotBeNull($"{operationType.Name} must publish its application definition.");
        definition.PropertyType.ShouldBe(definitionType);
    }

    private static void AssertHostNeutral(Type type)
    {
        var forbiddenPrefixes = new[] { "System.CommandLine", "ModelContextProtocol" };
        var referencedTypes = type.GetProperties().Select(property => property.PropertyType)
            .Concat(type.GetConstructors().SelectMany(constructor =>
                constructor.GetParameters().Select(parameter => parameter.ParameterType)));

        foreach (var referencedType in referencedTypes)
            foreach (var prefix in forbiddenPrefixes)
            {
                string? referencedNamespace = referencedType.Namespace;
                string.Equals(referencedNamespace, prefix, StringComparison.Ordinal).ShouldBeFalse();
                (referencedNamespace?.StartsWith(prefix + ".", StringComparison.Ordinal) ?? false)
                    .ShouldBeFalse();
            }
    }
}
