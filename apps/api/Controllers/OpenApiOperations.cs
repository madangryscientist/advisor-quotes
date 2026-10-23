using Api.Advisers.Services;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Api.Controllers;

/// <summary>The parts of the OpenAPI document that attributes alone cannot say: the adviser header, and the idempotency header pair.</summary>
public static class OpenApiOperations
{
    public static IServiceCollection AddControllerOperationDescriptions(this IServiceCollection services) =>
        services.ConfigureAll<OpenApiOptions>(options => options.AddOperationTransformer((operation, context, _) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            if (metadata.OfType<RequireAdviserAttribute>().Any())
            {
                (operation.Parameters ??= []).Add(new OpenApiParameter
                {
                    Name = CurrentAdviser.HeaderName,
                    In = ParameterLocation.Header,
                    Required = true,
                    Description = "The adviser making the request.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                });
            }

            if (metadata.OfType<IdempotentAttribute>().Any())
            {
                // The binder already lists the header as an optional string; it cannot say "required only when creating".
                var key = operation.Parameters!.Single(p => p.Name == QuoteCommandsController.IdempotencyKeyHeader);
                key.Description = "Required when creating a quote (the command has no quoteId); ignored when editing a draft.";
                (((OpenApiResponse)operation.Responses!["200"]).Headers ??= new Dictionary<string, IOpenApiHeader>())[QuoteCommandsController.ReplayedHeader] = new OpenApiHeader
                {
                    Description = "\"true\" when an earlier create with the same Idempotency-Key is returned instead of creating again.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                };
            }

            return Task.CompletedTask;
        }));
}
