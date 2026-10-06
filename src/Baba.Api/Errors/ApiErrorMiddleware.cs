using Baba.Application;
using Baba.Application.Companies;

namespace Baba.Api.Errors;

/// <summary>What the UI receives when something goes wrong: a stable <c>problem</c> code to translate, never a stack trace.</summary>
public sealed record ApiProblem(string Problem, string Message, IReadOnlyList<ValidationIssue>? Issues = null);

/// <summary>Turns known failures into clear HTTP answers. Anything unexpected becomes a plain 500 without details.</summary>
public sealed class ApiErrorMiddleware(RequestDelegate next, ILogger<ApiErrorMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception e) when (!context.Response.HasStarted && e is not OperationCanceledException)
        {
            var (status, problem) = Map(e);
            if (status == StatusCodes.Status500InternalServerError)
                logger.LogError(e, "Unhandled error in {Path}", context.Request.Path);

            context.Response.Clear();
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(problem);
        }
    }

    private static (int Status, ApiProblem Problem) Map(Exception exception) => exception switch
    {
        ValidationException v => (StatusCodes.Status400BadRequest, new ApiProblem("Validation", v.Message, v.Issues)),
        CompanyFileException c => (StatusFor(c.Problem), new ApiProblem(c.Problem.ToString(), c.Message)),
        NotFoundException n => (StatusCodes.Status404NotFound, new ApiProblem("NotFound", n.Message)),
        InvalidOperationException { Message: var m } when m.Contains("already open", StringComparison.OrdinalIgnoreCase)
            => (StatusCodes.Status409Conflict, new ApiProblem("CompanyAlreadyOpen", m)),
        BadHttpRequestException b => (StatusCodes.Status400BadRequest, new ApiProblem("BadRequest", b.Message)),
        _ => (StatusCodes.Status500InternalServerError, new ApiProblem("Unexpected", "Something went wrong.")),
    };

    private static int StatusFor(CompanyFileProblem problem) => problem switch
    {
        CompanyFileProblem.FileNotFound => StatusCodes.Status404NotFound,
        CompanyFileProblem.FileAlreadyExists => StatusCodes.Status409Conflict,
        CompanyFileProblem.CreatedByNewerVersion => StatusCodes.Status409Conflict,
        CompanyFileProblem.NoCompanyOpen => StatusCodes.Status409Conflict,
        CompanyFileProblem.OpenElsewhere => StatusCodes.Status423Locked,
        CompanyFileProblem.WrongPasswordOrNotABabaFile => StatusCodes.Status403Forbidden,
        CompanyFileProblem.NotABabaFile => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status400BadRequest,
    };
}
