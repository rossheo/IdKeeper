using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace IdKeeper.Common.Exceptions;

public sealed class GlobalExceptionHandler(
	IProblemDetailsService problemDetailsService,
	ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
	public async ValueTask<bool> TryHandleAsync(
		HttpContext httpContext,
		Exception exception,
		CancellationToken cancellationToken)
	{
		logger.LogError(exception, "Unhandled exception occurred.");

		Int32 statusCode = GetStatusCodeForException(exception);

		httpContext.Response.StatusCode = statusCode;

		string detailMessage = GetDetailMessage(httpContext, exception, statusCode);

		return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
		{
			HttpContext = httpContext,
			Exception = exception,
			ProblemDetails = new ProblemDetails
			{
				Type = exception.GetType().Name,
				Title = GetTitleForStatusCode(statusCode),
				Detail = detailMessage
			}
		});
	}

	private static Int32 GetStatusCodeForException(Exception exception)
	{
		string exceptionTypeName = exception.GetType().Name;

		return exception switch
		{
			ArgumentException => StatusCodes.Status400BadRequest,
			KeyNotFoundException => StatusCodes.Status404NotFound,
			UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
			ApplicationException => StatusCodes.Status400BadRequest,
			_ => exceptionTypeName switch
			{
				"ValidationException" => StatusCodes.Status400BadRequest,
				"FluentValidation.ValidationException" => StatusCodes.Status400BadRequest,

				"DbUpdateConcurrencyException" => StatusCodes.Status409Conflict,
				"DbUpdateException" => StatusCodes.Status409Conflict,

				_ => StatusCodes.Status500InternalServerError
			}
		};
	}

	private static string GetTitleForStatusCode(Int32 statusCode) => statusCode switch
	{
		StatusCodes.Status400BadRequest => "Bad Request",
		StatusCodes.Status401Unauthorized => "Unauthorized",
		StatusCodes.Status404NotFound => "Not Found",
		StatusCodes.Status409Conflict => "Conflict",
		_ => "Internal Server Error"
	};

	private static string GetDetailMessage(HttpContext httpContext, Exception exception, Int32 statusCode)
	{
		bool isAuthenticated = httpContext.User?.Identity?.IsAuthenticated ?? false;

		if (isAuthenticated && statusCode >= 400 && statusCode < 500)
		{
			return exception.Message;
		}

		return statusCode switch
		{
			StatusCodes.Status400BadRequest => "The request contains invalid data.",
			StatusCodes.Status401Unauthorized => "Authentication is required.",
			StatusCodes.Status404NotFound => "The requested resource was not found.",
			StatusCodes.Status409Conflict => "A conflict occurred while processing the request.",
			_ => "An internal error occurred. Please try again later."
		};
	}
}