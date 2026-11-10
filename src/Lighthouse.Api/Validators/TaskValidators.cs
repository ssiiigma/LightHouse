using FluentValidation;
using Lighthouse.Shared.Contracts.Tasks;

namespace Lighthouse.Api.Validators;

public sealed class UpsertTaskRequestValidator : AbstractValidator<UpsertTaskRequest>
{
    public UpsertTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.EstimatedMinutes).InclusiveBetween(1, 1440).When(x => x.EstimatedMinutes.HasValue);
        RuleFor(x => x.Subtasks).NotNull();
        RuleForEach(x => x.Subtasks).ChildRules(s => s.RuleFor(y => y.Title).NotEmpty().MaximumLength(200));
    }
}