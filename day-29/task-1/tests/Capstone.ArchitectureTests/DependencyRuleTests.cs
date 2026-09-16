using System.Reflection;
using NetArchTest.Rules;

namespace Capstone.ArchitectureTests;

// These tests enforce the dependency rule with a compiler-checkable fact (assembly
// references), not with a comment or a diagram. If someone adds a ProjectReference
// that violates the rule, `dotnet test` fails here - see capstone/README.md,
// "dependency rules and how they're enforced".
public sealed class DependencyRuleTests
{
    private static readonly Assembly ProcurementDomain = typeof(Capstone.Procurement.Domain.PurchaseOrder).Assembly;
    private static readonly Assembly ProcurementApplication = typeof(Capstone.Procurement.Application.IPurchaseOrderCapacityGateway).Assembly;
    private static readonly Assembly ProcurementInfrastructure = typeof(Capstone.Procurement.Infrastructure.InMemoryPurchaseOrderRepository).Assembly;
    private static readonly Assembly InvoicingDomain = typeof(Capstone.Invoicing.Domain.Invoice).Assembly;
    private static readonly Assembly InvoicingApplication = typeof(Capstone.Invoicing.Application.UseCases.SubmitInvoiceUseCase).Assembly;
    private static readonly Assembly InvoicingInfrastructure = typeof(Capstone.Invoicing.Infrastructure.InMemoryInvoiceRepository).Assembly;

    private static readonly string[] AllOtherLayers =
    [
        "Capstone.Procurement.Application",
        "Capstone.Procurement.Infrastructure",
        "Capstone.Invoicing.Application",
        "Capstone.Invoicing.Infrastructure",
        "Capstone.Web",
    ];

    [Fact]
    public void Domain_Assemblies_Do_Not_Depend_On_Application_Infrastructure_Or_Web()
    {
        foreach (var domainAssembly in new[] { ProcurementDomain, InvoicingDomain })
        {
            var result = Types.InAssembly(domainAssembly)
                .Should()
                .NotHaveDependencyOnAny(AllOtherLayers)
                .GetResult();

            Assert.True(result.IsSuccessful, Describe(domainAssembly, result));
        }
    }

    [Fact]
    public void Invoicing_Modules_Never_Depend_On_Procurement_Except_Its_Own_Infrastructure()
    {
        // Invoicing.Domain and Invoicing.Application must have zero knowledge of
        // Procurement - not even its Application-layer published DTOs. The
        // ProcurementCapacityAdapter in Invoicing.Infrastructure is the one
        // permitted crossing point (asserted positively below).
        foreach (var invoicingAssembly in new[] { InvoicingDomain, InvoicingApplication })
        {
            var result = Types.InAssembly(invoicingAssembly)
                .Should()
                .NotHaveDependencyOnAny("Capstone.Procurement.Domain", "Capstone.Procurement.Application", "Capstone.Procurement.Infrastructure")
                .GetResult();

            Assert.True(result.IsSuccessful, Describe(invoicingAssembly, result));
        }
    }

    [Fact]
    public void Procurement_Never_Depends_On_Invoicing()
    {
        // The dependency is one-directional: Procurement doesn't know Invoicing
        // exists, in any layer, including Infrastructure.
        foreach (var procurementAssembly in new[] { ProcurementDomain, ProcurementApplication, ProcurementInfrastructure })
        {
            var result = Types.InAssembly(procurementAssembly)
                .Should()
                .NotHaveDependencyOnAny("Capstone.Invoicing.Domain", "Capstone.Invoicing.Application", "Capstone.Invoicing.Infrastructure")
                .GetResult();

            Assert.True(result.IsSuccessful, Describe(procurementAssembly, result));
        }
    }

    [Fact]
    public void Application_Assemblies_Do_Not_Depend_On_Infrastructure_Or_Web()
    {
        foreach (var applicationAssembly in new[] { ProcurementApplication, InvoicingApplication })
        {
            var result = Types.InAssembly(applicationAssembly)
                .Should()
                .NotHaveDependencyOnAny("Capstone.Procurement.Infrastructure", "Capstone.Invoicing.Infrastructure", "Capstone.Web")
                .GetResult();

            Assert.True(result.IsSuccessful, Describe(applicationAssembly, result));
        }
    }

    [Fact]
    public void Invoicing_Infrastructure_Is_The_One_Project_Permitted_To_Cross_The_Module_Boundary()
    {
        // Positive check, not just an absence of violations: the adapter this rule
        // exists to permit actually has to be there, or the "only one crossing
        // point" claim in DESIGN.md would be untested.
        var referencesProcurementApplication = Types.InAssembly(InvoicingInfrastructure)
            .That()
            .HaveName("ProcurementCapacityAdapter")
            .Should()
            .HaveDependencyOn("Capstone.Procurement.Application")
            .GetResult();

        Assert.True(referencesProcurementApplication.IsSuccessful, Describe(InvoicingInfrastructure, referencesProcurementApplication));
    }

    private static string Describe(Assembly assembly, TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : $"{assembly.GetName().Name} violated the rule via: {string.Join(", ", result.FailingTypeNames ?? [])}";
}
