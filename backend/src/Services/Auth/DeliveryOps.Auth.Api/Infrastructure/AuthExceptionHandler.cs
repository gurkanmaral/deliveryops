using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Auth.Api.Infrastructure;

public sealed class AuthExceptionHandler(ILogger<AuthExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        (int status, string title) = exception switch
        {
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Kimlik kaydı başka bir işlem tarafından değiştirildi"),
            DbUpdateException => (StatusCodes.Status409Conflict, "Kimlik veritabanı işlemi tamamlanamadı"),
            ArgumentException => (StatusCodes.Status400BadRequest, "İstek doğrulanamadı"),
            _ => (StatusCodes.Status500InternalServerError, "Beklenmeyen bir kimlik servisi hatası oluştu")
        };
        logger.LogError(exception, "Auth request failed with status {StatusCode}", status);
        ProblemDetails problem = new()
        {
            Status = status,
            Title = title,
            Detail = status < 500 || environment.IsDevelopment() ? exception.Message : null,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
