using FluentValidation;
using TelegramGateway.Api.Contracts;

namespace TelegramGateway.Api.Validators;

public sealed class DigestValidator : AbstractValidator<DigestRequest>
{
    public DigestValidator()
    {
        RuleFor(request => request.Title).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Date).NotEqual(default(DateOnly));
        RuleFor(request => request.Language).NotEmpty().Matches("^[a-zA-Z]{2,3}(-[a-zA-Z0-9]{2,8})?$").MaximumLength(16);
        RuleFor(request => request.Content).NotEmpty().MaximumLength(100000);
        RuleFor(request => request.Format).Must(value => value is "plain" or "html" or "markdown");
        RuleFor(request => request.IdempotencyKey).Matches("^[a-zA-Z0-9._:-]{1,128}$").When(request => request.IdempotencyKey is not null);
    }
}
