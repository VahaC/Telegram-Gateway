using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using AgentGateway.Api.Contracts;
using AgentGateway.Api.Contracts.News;
using AgentGateway.Api.Http;
using AgentGateway.Api.Services.News;
using AgentGateway.Core.Options;

namespace AgentGateway.Api.Extensions;

public static class NewsEndpointExtensions
{
    /// <summary>A 45-item Ukrainian issue is about 150-250 KiB; every other route keeps the 128 KiB limit.</summary>
    public const long MaxIssueBytes = 1024 * 1024;

    public static void MapNewsEndpoints(this WebApplication application)
    {
        var news = application.MapGroup("/api/news").AddEndpointFilter(async (context, next) =>
            context.HttpContext.RequestServices.GetRequiredService<IOptions<NewsOptions>>().Value.Enabled
                ? await next(context) : Results.NotFound());
        news.MapPost("/issues", async (HttpRequest request, NewsService service, IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> json,
                CancellationToken cancellationToken) =>
            {
                var (issue, error) = await ReadIssueAsync(request, json.Value.SerializerOptions, cancellationToken);
                return error ?? MapResult(await service.SubmitAsync(issue!, cancellationToken));
            })
            .WithName("SubmitNewsIssue")
            .WithDescription("Submit a whole news issue (contract v1). 202 queues publication to WordPress; 400 structure, 422 editorial rules, 409 duplicates or an existing date.")
            .Produces<NewsIssueStatusResponse>(202).Produces<NewsIssueStatusResponse>(200)
            .Produces<NewsRejectionResponse>(400).Produces<NewsRejectionResponse>(409).Produces<NewsRejectionResponse>(422);
        news.MapPut("/issues/{date}", async (string date, HttpRequest request, NewsService service,
                IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> json, CancellationToken cancellationToken) =>
            {
                if (ParseDate(date) is not { } parsed) return ApiErrors.BadRequest("Date must use yyyy-MM-dd.");
                var (issue, error) = await ReadIssueAsync(request, json.Value.SerializerOptions, cancellationToken);
                return error ?? MapResult(await service.ReplaceAsync(parsed, issue!, cancellationToken));
            })
            .WithName("ReplaceNewsIssue")
            .WithDescription("Replace an existing issue. Same validation; duplicates ignore the issue's own items. Bumps the revision and republishes.")
            .Produces<NewsIssueStatusResponse>(202).Produces<NewsIssueStatusResponse>(200)
            .Produces<NewsRejectionResponse>(400).Produces<NewsRejectionResponse>(409).Produces<NewsRejectionResponse>(422);
        news.MapGet("/issues/{date}", async (string date, NewsService service, CancellationToken cancellationToken) =>
            {
                if (ParseDate(date) is not { } parsed) return ApiErrors.BadRequest("Date must use yyyy-MM-dd.");
                return await service.GetStatusAsync(parsed, cancellationToken) is { } status ? Results.Ok(status) : Results.NotFound();
            })
            .WithName("NewsIssueStatus").Produces<NewsIssueStatusResponse>();
        news.MapPost("/issues/{date}/republish", async (string date, NewsService service, CancellationToken cancellationToken) =>
            {
                if (ParseDate(date) is not { } parsed) return ApiErrors.BadRequest("Date must use yyyy-MM-dd.");
                return MapResult(await service.RepublishAsync(parsed, cancellationToken));
            })
            .WithName("RepublishNewsIssue")
            .WithDescription("Queue the unfinished calls of a Failed or PartiallyPublished issue again; a no-op for any other state.")
            .Produces<NewsIssueStatusResponse>(202).Produces<NewsIssueStatusResponse>(200);
        news.MapDelete("/issues/{date}", async (string date, NewsService service, CancellationToken cancellationToken) =>
            {
                if (ParseDate(date) is not { } parsed) return ApiErrors.BadRequest("Date must use yyyy-MM-dd.");
                return MapResult(await service.DeleteFixtureAsync(parsed, cancellationToken));
            })
            .WithName("DeleteNewsFixture")
            .WithDescription("Remove the test fixture (scripts/check-news.sh --cleanup). Any issue that is not the fixture is refused with 409.")
            .Produces<NewsIssueStatusResponse>(202).Produces<ApiErrorResponse>(409);
        news.MapGet("/history", async (NewsService service, CancellationToken cancellationToken, int days = 30) =>
                await service.GetHistoryAsync(days, cancellationToken) is { } history ? Results.Ok(history)
                    : ApiErrors.BadRequest($"Days must be between 1 and {NewsService.MaxHistoryDays}."))
            .WithName("NewsHistory").WithDescription("Everything published in the last days (newest first), to skip known events before researching them.")
            .Produces<NewsHistoryResponse>();
        news.MapPost("/check", async (NewsCheckRequest request, NewsService service, CancellationToken cancellationToken) =>
            {
                var (response, error) = await service.CheckAsync(request, cancellationToken);
                return error is null ? Results.Ok(response) : ApiErrors.BadRequest(error);
            })
            .WithName("CheckNewsCandidates").WithDescription("Which candidate URLs or eventKeys are already in the registry.")
            .Produces<NewsCheckResponse>().Produces<ApiErrorResponse>(400);
    }

    private static async Task<(NewsIssueRequest? Issue, IResult? Error)> ReadIssueAsync(HttpRequest request, JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType()) return (null, ApiErrors.BadRequest("Send the issue as application/json."));
        try
        {
            var issue = await request.ReadFromJsonAsync<NewsIssueRequest>(options, cancellationToken);
            return issue is null ? (null, ApiErrors.BadRequest("The news issue is missing.")) : (issue, null);
        }
        catch (JsonException exception)
        {
            // The serializer names the JSON path and the problem, e.g. an unknown property; that is what the caller must fix.
            var detail = exception.Message.Length > 300 ? exception.Message[..300] : exception.Message;
            return (null, Results.Json(new NewsRejectionResponse("The news issue is not valid JSON for contract v1.", Errors: [detail]), statusCode: 400));
        }
    }

    private static DateOnly? ParseDate(string value)
        => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;

    private static IResult MapResult(NewsSubmitResult result) => result.Status switch
    {
        NewsSubmitStatus.Accepted => Results.Accepted($"/api/news/issues/{result.Issue!.Date:yyyy-MM-dd}", result.Issue),
        NewsSubmitStatus.Existing => Results.Ok(result.Issue),
        NewsSubmitStatus.NotFound => Results.NotFound(),
        NewsSubmitStatus.Invalid => Results.Json(new NewsRejectionResponse(result.Error!, Errors: result.Errors), statusCode: 400),
        NewsSubmitStatus.Rejected => Results.Json(new NewsRejectionResponse(result.Error!, Violations: result.Violations), statusCode: 422),
        NewsSubmitStatus.Duplicate => Results.Json(new NewsRejectionResponse(result.Error!, Duplicates: result.Duplicates), statusCode: 409),
        _ => ApiErrors.Conflict(result.Error!)
    };
}
