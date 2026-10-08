using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TelegramGateway.Api.OpenApi;

public sealed class ApiKeyDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "Telegram News Delivery Gateway";
        document.Info.Description = "Authenticated delivery to one configured private chat. Accepted means queued; poll delivery status to confirm Telegram message IDs.";
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey, Name = "X-Api-Key", In = ParameterLocation.Header,
            Description = "Configured API_KEY. Never send it in the URL."
        };
        foreach (var path in document.Paths.Where(path => path.Key is not ("/api/health" or "/api/ready")))
            foreach (var operation in path.Value.Operations?.Values.AsEnumerable() ?? [])
                operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("ApiKey", document)] = [] }];
        return Task.CompletedTask;
    }
}
