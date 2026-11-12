using FluentValidation;
using Lighthouse.Shared.Contracts.Recommendations;

namespace Lighthouse.Api.Validators;

public sealed class FeedbackRequestValidator : AbstractValidator<FeedbackRequest>
{
    public FeedbackRequestValidator()
    {
        RuleFor(x => x.RecommendationKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Action).IsInEnum();
    }
}