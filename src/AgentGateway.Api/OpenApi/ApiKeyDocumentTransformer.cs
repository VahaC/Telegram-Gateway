using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AgentGateway.Api.OpenApi;

public sealed class ApiKeyDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "AgentGateway";
        document.Info.Description = "Authenticated Telegram delivery to one configured private chat, and news issue publication to WordPress. Accepted means queued; poll the status routes to confirm.";
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
