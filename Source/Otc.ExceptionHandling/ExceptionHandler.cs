using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Otc.DomainBase.Exceptions;
using Otc.ExceptionHandling.Abstractions;

namespace Otc.ExceptionHandling
{
    public class ExceptionHandler : IExceptionHandler
    {
        private readonly ILogger logger;
        private readonly IExceptionHandlerConfiguration configuration;

        public ExceptionHandler(ILoggerFactory loggerFactory,
            IExceptionHandlerConfiguration configuration)
        {
            logger = loggerFactory?.CreateLogger<ExceptionHandler>();

            this.configuration = configuration;

            if (logger == null)
                throw new ArgumentNullException(nameof(loggerFactory));
        }

        public async Task<int> HandleExceptionAsync(Exception exception, HttpContext httpContext)
        {
            ExceptionHandlerBehavior? behavior;
            (httpContext.Response.StatusCode, exception, behavior) =
                await ValidateConfigurationsAsync(httpContext.Response.StatusCode, exception);

            if (exception is AggregateException)
            {
                var aggregateException = exception as AggregateException;

                foreach (var innerException in aggregateException.InnerExceptions)
                {
                    await HandleExceptionAsync(innerException, httpContext);
                }
            }
            else
            {
                if (!behavior.HasValue)
                {
                    if (exception is UnauthorizedAccessException)
                    {
                        httpContext.Response.StatusCode = 403;

                        return await GenerateUnauthorizadeExceptionResponseAsync(httpContext);
                    }

                    behavior = await IdentifyBehaviorAsync(exception, httpContext);
                }

                switch (behavior)
                {
                    case ExceptionHandlerBehavior.ClientError:
                        return await GenerateCoreExceptionResponseAsync(exception, httpContext);

                    case ExceptionHandlerBehavior.ServerError:
                        return await GenerateInternalErrorResponseAsync(exception, httpContext);

                }
            }

            return httpContext.Response.StatusCode;
        }

        private Task<ExceptionHandlerBehavior> IdentifyBehaviorAsync(Exception exception,
            HttpContext httpContext)
        {
            ExceptionHandlerBehavior behavior;

            if (exception is CoreException)
            {
                behavior = ExceptionHandlerBehavior.ClientError;

                httpContext.Response.StatusCode = 400;
            }
            else
            {
                behavior = ExceptionHandlerBehavior.ServerError;
                httpContext.Response.StatusCode = 500;
            }

            return Task.FromResult(behavior);
        }

        private async Task<int> GenerateCoreExceptionResponseAsync(Exception e, HttpContext httpContext)
        {
            logger.LogInformation(e, "Ocorreu um erro de negócio.");

            await GenerateResponseAsync(e, httpContext);

            return httpContext.Response.StatusCode;
        }

        /// <summary>
        /// Retorna um httpStatusCode 401.
        /// </summary>
        /// <param name="e">Inner Exception</param>
        /// <param name="httpContext">HttpContext</param>
        /// <returns></returns>
        private async Task<int> GenerateUnauthorizadeExceptionResponseAsync(HttpContext httpContext)
        {
            logger.LogInformation("Ocorreu um acesso não autorizado.");

            var forbidden = new
            {
                Key = "Forbidden",
                Message = "Access to this resource is forbidden."
            };

            await GenerateResponseAsync(forbidden, httpContext);

            return httpContext.Response.StatusCode;
        }

        private bool IsDevelopmentEnvironment()
            => Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

        private async Task<int> GenerateInternalErrorResponseAsync(Exception e, HttpContext httpContext)
        {
            Exception exception = e;
            Guid logEntryId = Guid.NewGuid();

            logger.LogError(e, "{LogEntryId}: Ocorreu um erro não esperado.", logEntryId);

            var internalError = new InternalError()
            {
                LogEntryId = logEntryId,
                Exception = (IsDevelopmentEnvironment() ? exception.GetBaseException() : null)
            };

            await GenerateResponseAsync(internalError, httpContext);

            return httpContext.Response.StatusCode;
        }

        private async Task GenerateResponseAsync(object output, HttpContext httpContext)
        {
            //https://devblogs.microsoft.com/dotnet/whats-next-for-system-text-json/
            //https://docs.microsoft.com/en-gb/dotnet/standard/serialization/system-text-json-migrate-from-newtonsoft-how-to?pivots=dotnet-5-0

            var jsonSerializerSettings = new JsonSerializerSettings()
            {
                //NÂO EXISTE UMA SOLUÇÂO para resolver o contrato em tempo de serialização/deserialização
                ContractResolver =
                    new CoreExceptionJsonContractResolver()
                    {
                        IgnoreSerializableInterface = true
                    },
                //NÂO EXISTE UMA SOLUÇÂO system.text.json por hora
                //EM ABERTO: https://github.com/dotnet/runtime/issues/30820
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
                MaxDepth = 10,
                Formatting = !IsDevelopmentEnvironment() ? Formatting.None : Formatting.Indented
            };

            var message = JsonConvert.SerializeObject(output, jsonSerializerSettings);

            //var jsonSerializerSettings = new JsonSerializerOptions()
            //{

            //    //##################
            //    //TO SEARCH //TO SEARCH IN SYSTEM.text.Json
            //    //https://docs.microsoft.com/en-gb/dotnet/standard/serialization/system-text-json-customize-properties#use-camel-case-for-all-json-property-names
            //    //https://docs.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializeroptions.propertynamingpolicy?view=net-5.0#System_Text_Json_JsonSerializerOptions_PropertyNamingPolicy

            //    //      Camel-case naming policy with JsonNamingPolicy.CamelCase
            //    //      Serialization.JsonConverter GetConverter(Type typeToConvert);
            //    //      PropertyNameCaseInsensitive
            //    //      PropertyNamingPolicy

            //    //ContractResolver =
            //    //    new CoreExceptionJsonContractResolver()
            //    //    {
            //    //        IgnoreSerializableInterface = true
            //    //    },
            //    //##################

            //    //##################
            //    //TO SEARCH IN SYSTEM.text.Json

            //    //https://www.google.com/search?q=system.text.json+ignore+Reference+Loop+Handling&oq=system.text.json+ignore+Reference+Loop+Handling&aqs=chrome..69i57.9508j0j1&sourceid=chrome&ie=UTF-8
            //    //https://docs.microsoft.com/en-gb/dotnet/standard/serialization/system-text-json-migrate-from-newtonsoft-how-to?pivots=dotnet-5-0#preserve-object-references-and-handle-loops
            //    //ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
            //    //https://github.com/dotnet/docs/tree/9d5e88edbd7f12be463775ffebbf07ac8415fe18/docs/standard/serialization/snippets/system-text-json-how-to-5-0/csharp
            //    //https://github.com/dotnet/docs/blob/9d5e88edbd7f12be463775ffebbf07ac8415fe18/docs/standard/serialization/snippets/system-text-json-how-to-5-0/csharp/GuidReferenceResolverExample.cs
            //    //PERGUNTA: DEixar nulo funciona como ignorar?

            //    //PROBLEMA EM ABRTO
            //    //https://github.com/dotnet/runtime/issues/30820

            //    //      ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            //    //##################


            //    IgnoreNullValues = true, ////NullValueHandling = NullValueHandling.Ignore,
            //    MaxDepth = 10,
            //    WriteIndented = !IsDevelopmentEnvironment() ////Formatting = !IsDevelopmentEnvironment() ? Formatting.None : Formatting.Indented
            //};

            //var message = JsonSerializer.Serialize(output, jsonSerializerSettings);
            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.WriteAsync(message, Encoding.UTF8);
        }

        private Task<(int statusCode, Exception exception, ExceptionHandlerBehavior? behavior)>
            ValidateConfigurationsAsync(int statusCode, Exception e)
        {
            Exception exception = e;
            int finalStatusCode = statusCode;
            ExceptionHandlerBehavior? behavior = null;

            //Executar eventos
            if (configuration != null)
            {
                if (configuration.Events.Any())
                {
                    foreach (var @event in configuration.Events)
                    {
                        if (@event.IsElegible(statusCode, e))
                        {
                            (finalStatusCode, exception, behavior) = @event.Intercept(statusCode, e);
                        }
                    }
                }

                if (configuration.HasBehaviors)
                {
                    var behaviorResult = configuration.ValidateBehavior(e);

                    if (behaviorResult != null)
                    {
                        behavior = behaviorResult.Behavior;

                        finalStatusCode = behaviorResult.StatusCode;
                    }
                }
            }

            return Task.FromResult((finalStatusCode, exception, behavior));

        }
    }
}
