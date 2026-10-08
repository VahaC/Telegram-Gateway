using FluentValidation;
using TelegramGateway.Api.Contracts;

namespace TelegramGateway.Api.Validators;

public sealed class MessageValidator : AbstractValidator<MessageRequest>
{
    public MessageValidator()
    {
        RuleFor(request => request.Text).NotEmpty().MaximumLength(100000);
        RuleFor(request => request.Format).Must(value => value is "plain" or "html" or "markdown");
        RuleFor(request => request.IdempotencyKey).Matches("^[a-zA-Z0-9._:-]{1,128}$").When(request => request.IdempotencyKey is not null);
    }
}
