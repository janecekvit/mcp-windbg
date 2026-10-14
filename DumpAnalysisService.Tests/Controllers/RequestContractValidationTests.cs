using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shared.Models;

namespace DumpAnalysisService.Tests.Controllers;

/// <summary>
/// Runs MVC model validation on every REST request record. ASP.NET Core throws on a record whose
/// validation attribute sits on the property instead of the constructor parameter
/// (<c>[property: Required]</c>), which turns every request to that endpoint into a 500.
/// </summary>
public class RequestContractValidationTests
{
    public static IEnumerable<object[]> Requests() =>
    [
        [new LoadDumpRequest(@"C:\dumps\app.dmp"), true],
        [new LoadDumpRequest(null!), false],
        [new ExecuteCommandRequest("abc12345", "k"), true],
        [new ExecuteCommandRequest("abc12345", null!), false],
        [new BasicAnalysisRequest("abc12345"), true],
        [new PredefinedAnalysisRequest("abc12345", AnalysisType.Threads), true],
        [new CloseSessionRequest(null!), false],
        [new TriageDumpRequest(@"C:\dumps\app.dmp"), true],
        [new TriageDumpRequest(null!), false],
    ];

    [Theory]
    [MemberData(nameof(Requests))]
    public void Validate_RequestRecord_ReportsRequiredFieldsWithoutThrowing(object request, bool expectedValid)
    {
        var services = new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider();
        var validator = services.GetRequiredService<IObjectModelValidator>();
        var actionContext = new ActionContext(
            new DefaultHttpContext { RequestServices = services }, new RouteData(), new ActionDescriptor());

        validator.Validate(actionContext, validationState: null, prefix: string.Empty, model: request);

        Assert.Equal(expectedValid, actionContext.ModelState.IsValid);
    }
}
