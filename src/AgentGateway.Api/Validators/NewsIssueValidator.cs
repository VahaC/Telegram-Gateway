using FluentValidation;
using AgentGateway.Api.Contracts.News;

namespace AgentGateway.Api.Validators;

/// <summary>Structure of news contract v1 (400). The editorial rules live in NewsEditorialGate (422).</summary>
public sealed class NewsIssueValidator : AbstractValidator<NewsIssueRequest>
{
    public NewsIssueValidator()
    {
        RuleFor(issue => issue.ContractVersion).Equal(1).WithMessage("must be 1.");
        RuleFor(issue => issue.Date).NotEqual(default(DateOnly)).WithMessage("is required (yyyy-MM-dd).");
        RuleFor(issue => issue.Title).NotEmpty().MaximumLength(200).Must(NewsRules.IsPlainText).WithMessage("must be plain text.");
        RuleFor(issue => issue.Language).NotEmpty().Matches("^[a-zA-Z]{2,3}(-[a-zA-Z0-9]{2,8})?$").MaximumLength(16);
        RuleFor(issue => issue.Intro).MaximumLength(1000).Must(NewsRules.IsPlainText).WithMessage("must be plain text.");
        RuleFor(issue => issue.Items).NotNull().Must(items => items is { Count: >= 1 and <= 80 }).WithMessage("must contain 1 to 80 items.");
        RuleForEach(issue => issue.Items).NotNull().SetValidator(new NewsItemValidator());
        RuleFor(issue => issue.Releases).Must(list => list is null || list.Count <= 40).WithMessage("must have at most 40 entries.");
        RuleForEach(issue => issue.Releases).NotNull().ChildRules(release =>
        {
            release.RuleFor(entry => entry.Name).NotEmpty().MaximumLength(100);
            release.RuleFor(entry => entry.Version).NotEmpty().MaximumLength(50);
            release.RuleFor(entry => entry.Date).NotEqual(default(DateOnly)).WithMessage("is required (yyyy-MM-dd).");
            release.RuleFor(entry => entry.Url).Must(url => NewsRules.IsWebUrl(url, httpsOnly: false))
                .WithMessage("must be an absolute http(s) URL of at most 2000 characters.");
            release.RuleFor(entry => entry.Note).MaximumLength(300).Must(NewsRules.IsPlainText).WithMessage("must be plain text.");
        });
        RuleFor(issue => issue.SecurityAlerts).Must(list => list is null || list.Count <= 40).WithMessage("must have at most 40 entries.");
        RuleForEach(issue => issue.SecurityAlerts).NotNull().ChildRules(alert =>
        {
            alert.RuleFor(entry => entry.Id).NotEmpty().MaximumLength(50);
            alert.RuleFor(entry => entry.Product).NotEmpty().MaximumLength(100);
            alert.RuleFor(entry => entry.Severity).Must(value => NewsRules.Severities.Contains(value))
                .WithMessage("must be critical, high, medium or low.");
            alert.RuleFor(entry => entry.Cvss).InclusiveBetween(0, 10).When(entry => entry.Cvss is not null);
            alert.RuleFor(entry => entry.Date).NotEqual(default(DateOnly)).WithMessage("is required (yyyy-MM-dd).");
            alert.RuleFor(entry => entry.Url).Must(url => NewsRules.IsWebUrl(url, httpsOnly: false))
                .WithMessage("must be an absolute http(s) URL of at most 2000 characters.");
            alert.RuleFor(entry => entry.Note).MaximumLength(300).Must(NewsRules.IsPlainText).WithMessage("must be plain text.");
        });
        RuleFor(issue => issue.Research!).ChildRules(research =>
        {
            research.RuleFor(entry => entry.Mode).MaximumLength(100);
            research.RuleFor(entry => entry.SearchPasses).GreaterThanOrEqualTo(0);
            research.RuleFor(entry => entry.Candidates).GreaterThanOrEqualTo(0);
            research.RuleFor(entry => entry.DuplicatesRemoved).GreaterThanOrEqualTo(0);
            research.RuleFor(entry => entry.WindowHours).GreaterThanOrEqualTo(0);
        }).When(issue => issue.Research is not null);
    }
}
