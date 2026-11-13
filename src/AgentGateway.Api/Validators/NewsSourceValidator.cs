using FluentValidation;
using AgentGateway.Api.Contracts.News;

namespace AgentGateway.Api.Validators;

public sealed class NewsSourceValidator : AbstractValidator<NewsSourceRequest>
{
    public NewsSourceValidator()
    {
        RuleFor(source => source.Url).Must(url => NewsRules.IsWebUrl(url, httpsOnly: false))
            .WithMessage("must be an absolute http(s) URL of at most 2000 characters.");
        RuleFor(source => source.Name).NotEmpty().MaximumLength(100);
    }
}
