using System.Reflection;
using NetArchTest.Rules;

namespace OrderTracker.ArchitectureTests;

/// <summary>
/// Enforces the Clean Architecture dependency rule: source code dependencies point inwards only.
///
///   Api ──► Infrastructure ──► Application ──► Domain
///    └───────────────────────────┘
/// </summary>
public class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(OrderTracker.Domain.Orders.Order).Assembly;
    private static readonly Assembly Application = typeof(OrderTracker.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(OrderTracker.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
        Assert.True(result.IsSuccessful, $"{assembly.GetName().Name} must not depend on {string.Join(", ", forbidden)}. " +
            $"Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Domain_depends_on_nothing_else_in_the_solution_or_any_framework() =>
        AssertNoDependency(Domain,
            "OrderTracker.Application", "OrderTracker.Infrastructure", "OrderTracker.Api",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions", "Npgsql");

    [Fact]
    public void Domain_has_no_references_beyond_the_base_class_library() =>
        Assert.DoesNotContain(Domain.GetReferencedAssemblies(), a => a.Name!.StartsWith("OrderTracker", StringComparison.Ordinal)
            || a.Name.StartsWith("Microsoft.Extensions", StringComparison.Ordinal)
            || a.Name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

    [Fact]
    public void Application_does_not_depend_on_infrastructure_web_or_persistence() =>
        AssertNoDependency(Application,
            "OrderTracker.Infrastructure", "OrderTracker.Api",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql", "System.Net.Http");

    [Fact]
    public void Infrastructure_does_not_depend_on_the_api() =>
        AssertNoDependency(Infrastructure, "OrderTracker.Api");

    [Fact]
    public void The_api_talks_to_use_cases_not_to_persistence() =>
        Assert.True(Types.InAssembly(Api)
            .That().ResideInNamespace("OrderTracker.Api.Endpoints")
            .ShouldNot().HaveDependencyOnAny("OrderTracker.Infrastructure", "Microsoft.EntityFrameworkCore", "OrderTracker.Domain.Orders.IOrderRepository")
            .GetResult().IsSuccessful);

    [Fact]
    public void Control_the_rules_above_can_see_real_dependencies()
    {
        // If the scanner couldn't read dependencies, every "should not depend" rule would pass vacuously.
        Assert.False(Types.InAssembly(Application).ShouldNot().HaveDependencyOn("OrderTracker.Domain").GetResult().IsSuccessful);
        Assert.False(Types.InAssembly(Infrastructure).ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore").GetResult().IsSuccessful);
    }

    [Fact]
    public void Domain_entities_and_value_objects_have_no_public_setters()
    {
        var violations = Domain.GetTypes()
            .Where(t => t.Namespace == "OrderTracker.Domain.Orders")
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.SetMethod is { IsPublic: true } setter && !setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit"))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        Assert.Empty(violations);
    }
}
