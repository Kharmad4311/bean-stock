using ErrorOr;
using FluentValidation;
using Mediator;

namespace BeanStock.Application.Common.Behaviors;

/// <summary>
/// Validates incoming messages with their FluentValidation validators before the handler runs.
/// Validation failures are returned as <see cref="ErrorType.Validation"/> errors, not thrown.
/// </summary>
/// <remarks>Only applies to messages whose response is an <see cref="IErrorOr"/>.</remarks>
public sealed class ValidationBehavior<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
    where TResponse : IErrorOr
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var errors = new List<Error>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(message, cancellationToken);
            errors.AddRange(result.Errors.Select(failure => Error.Validation(
                code: failure.PropertyName,
                description: failure.ErrorMessage)));
        }

        if (errors.Count == 0)
        {
            return await next(message, cancellationToken);
        }

        // ErrorOr<T> defines an implicit conversion from List<Error>; TResponse is only known at runtime.
        return (TResponse)(dynamic)errors;
    }
}
