using FluentValidation;
using MediatR;

namespace DeliveryOps.Core.Api.Infrastructure;

public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any()) return await next();
        ValidationContext<TRequest> context = new(request);
        var results = await Task.WhenAll(validators.Select(x => x.ValidateAsync(context, cancellationToken)));
        var failures = results.SelectMany(x => x.Errors).Where(x => x is not null).ToArray();
        if (failures.Length > 0) throw new ValidationException(failures);
        return await next();
    }
}
