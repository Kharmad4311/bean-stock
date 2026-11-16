using BeanStock.Application.Common.Errors;
using ErrorOr;

namespace BeanStock.Api.Http;

/// <summary>
/// Translates expected business errors into ProblemDetails responses.
/// </summary>
/// <remarks>
/// The stable error code is exposed in the <c>code</c> extension, along with the error metadata.
/// </remarks>
internal static class ErrorOrResults
{
    public static IResult ToProblem(this List<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (errors.Count == 0)
        {
            return TypedResults.Problem();
        }

        if (errors.TrueForAll(error => error.Type == ErrorType.Validation))
        {
            var validationErrors = errors
                .GroupBy(error => error.Code)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray());

            return TypedResults.ValidationProblem(validationErrors);
        }

        var firstError = errors[0];
        var extensions = new Dictionary<string, object?> { ["code"] = firstError.Code };
        foreach (var (key, value) in firstError.Metadata ?? [])
        {
            extensions[key] = value;
        }

        return TypedResults.Problem(
            statusCode: ToStatusCode(firstError),
            title: firstError.Description,
            extensions: extensions);
    }

    private static int ToStatusCode(Error error) => error.NumericType switch
    {
        ApplicationErrorTypes.Upstream => StatusCodes.Status502BadGateway,
        _ => error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        },
    };
}
