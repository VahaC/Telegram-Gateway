using FluentValidation;
using AgentGateway.Api.Contracts.News;
using AgentGateway.Core.News;

namespace AgentGateway.Api.Validators;

public sealed class NewsItemValidator : AbstractValidator<NewsItemRequest>
{
    public NewsItemValidator()
    {
        RuleFor(item => item.Number).GreaterThanOrEqualTo(1);
        RuleFor(item => item.Category).Must(value => NewsCategories.All.Contains(value))
            .WithMessage("must be one of " + string.Join(", ", NewsCategories.All) + ".");
        RuleFor(item => item.Importance).InclusiveBetween(1, 5);
        RuleFor(item => item.Title).NotEmpty().MaximumLength(200).Must(NewsRules.IsPlainText).WithMessage("must be plain text.");
        RuleFor(item => item.Summary).NotEmpty().MaximumLength(2000).Must(NewsRules.IsPlainText)
            .WithMessage("must be plain text without HTML or Markdown.");
        RuleFor(item => item.WhyItMatters).NotEmpty().MaximumLength(600).Must(NewsRules.IsPlainText)
            .WithMessage("must be plain text without HTML or Markdown.");
        RuleFor(item => item.PublishedOn).NotEqual(default(DateOnly)).WithMessage("is required (yyyy-MM-dd).");
        RuleFor(item => item.ReleaseStatus).Must(value => NewsRules.ReleaseStatuses.Contains(value))
            .When(item => item.ReleaseStatus is not null).WithMessage("must be one of " + string.Join(", ", NewsRules.ReleaseStatuses) + ".");
        RuleFor(item => item.Version).MaximumLength(50);
        RuleFor(item => item.EventKey).Must(NewsRules.IsEventKey)
            .WithMessage("must match ^[a-z0-9]+(-[a-z0-9]+)*$ and have 3 to 100 characters.");
        RuleFor(item => item.Source).NotNull().SetValidator(new NewsSourceValidator()!);
        RuleFor(item => item.ExtraSources).Must(list => list is null || list.Count <= 3).WithMessage("must have at most 3 entries.");
        RuleForEach(item => item.ExtraSources).NotNull().SetValidator(new NewsSourceValidator());
        RuleFor(item => item.Image!.Url).Must(url => NewsRules.IsWebUrl(url, httpsOnly: true))
            .When(item => item.Image is not null).WithMessage("must be an absolute https URL of at most 2000 characters.");
        RuleFor(item => item.Image!.Alt).MaximumLength(200).When(item => item.Image is not null);
        RuleFor(item => item.Tags).Must(list => list is null || list.Count <= 8).WithMessage("must have at most 8 entries.");
        RuleForEach(item => item.Tags).NotEmpty().MaximumLength(40);
    }
}
