using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Api.Infrastructure;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        (int status, string title) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "İstek doğrulanamadı"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Erişim reddedildi"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Kayıt başka bir işlem tarafından değiştirildi"),
            DbUpdateException => (StatusCodes.Status409Conflict, "Veritabanı işlemi tamamlanamadı"),
            _ => (StatusCodes.Status500InternalServerError, "Beklenmeyen bir hata oluştu")
        };
        logger.LogError(exception, "Request failed with status {StatusCode}", status);
        ProblemDetails problem = new()
        {
            Status = status,
            Title = title,
            Detail = status < 500 || environment.IsDevelopment() ? exception.Message : null,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        if (exception is ValidationException validation)
            problem.Extensions["errors"] = validation.Errors.GroupBy(x => x.PropertyName)
                .ToDictionary(x => x.Key, x => x.Select(failure => failure.ErrorMessage).ToArray());
        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
