using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using packages.Api.Enums;
using packages.Api.Responses;
using Packages.CustomErrors.Exceptions;
using Packages.CustomErrors.Exceptions.Base;
using Serilog;
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;

namespace Packages.GlobalExceptionHandler
{
    public static class Handler
    {
        private static Guid _responseKey;

        public static IApplicationBuilder UseCustomErrors(this IApplicationBuilder app, IHostEnvironment environment)
        {

            _responseKey = Guid.NewGuid();

            app.Use(WriteResponse);

            return app;
        }

        private static async Task WriteResponse(HttpContext context, Func<Task> next)
        {
            var exceptionDetails = context.Features.Get<IExceptionHandlerFeature>();
            var ex = exceptionDetails?.Error;

            if (ex != null)
            {
                context.Response.ContentType = "application/problem+json";

                var problem = ex.Demystify();

                var traceId = Activity.Current?.Id ?? context?.TraceIdentifier;
                if (traceId != null)
                {
                    problem.Extensions["traceId"] = traceId;
                }

                var problemResponse = new Response<string>
                {
                    ResponseKey = _responseKey,
                    ResponseCode = ResponseCode.Invalid,
                    ProblemDetails = problem
                };

                //Serialize the problem details object to the Response as JSON (using System.Text.Json)
                var stream = context.Response.Body;
                await JsonSerializer.SerializeAsync(stream, problemResponse).ConfigureAwait(false);
            }
        }

        private static ProblemDetails Demystify(this Exception exception)
        {
            return exception is BaseException baseException
                ? RetrieveBase(baseException)
                : RetrieveUnexpected(exception);
        }

        private static ProblemDetails RetrieveBase(BaseException exception)
        {
            Log.ForContext("Type", "Error").ForContext("Exception", exception, destructureObjects: true)
                .Error(exception, exception.Message + ". {@errorId}" + " exposedDetails " + exception.ExposedDetails, _responseKey);

            return new ProblemDetails
            {
                Title = exception.ExposedTitle,
                Type = exception.ErrorCode.GetErrorType(),
                Status = (int)exception.StatusCode,
                Detail = exception.ExposedDetails,
                Instance = $"errorId:{_responseKey}"
            };
        }

        private static ProblemDetails RetrieveUnexpected(Exception exception)
        {
            Log.ForContext("Type", "Error").ForContext("Exception", exception, destructureObjects: true)
                .Error(exception, exception.Message + ". {@errorId}", _responseKey);

            var unexpectedError = new UnexpectedErrorException(exception);

            return new ProblemDetails
            {
                Title = unexpectedError.ExposedTitle,
                Type = ErrorCodeEnum.Unexpected.GetErrorType(),
                Status = (int)unexpectedError.StatusCode,
                Detail = unexpectedError.ExposedDetails,
                Instance = $"errorId:{_responseKey}"
            };
        }
    }
}
