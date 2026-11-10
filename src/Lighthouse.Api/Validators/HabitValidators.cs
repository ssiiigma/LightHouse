using FluentValidation;
using Lighthouse.Shared.Contracts.Habits;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Api.Validators;

public sealed class UpsertHabitRequestValidator : AbstractValidator<UpsertHabitRequest>
{
    public UpsertHabitRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.FrequencyType).IsInEnum();
        RuleFor(x => x.ColorHex).Matches("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$").When(x => x.ColorHex is not null);
        RuleFor(x => x.Icon).MaximumLength(50);

        // Біти 0..6 = пн..нд: щонайменше один день.
        RuleFor(x => x.DaysOfWeekMask).InclusiveBetween(1, 127)
            .When(x => x.FrequencyType == HabitFrequencyType.SpecificDays);

        RuleFor(x => x.TimesPerWeek).NotNull().InclusiveBetween(1, 7)
            .When(x => x.FrequencyType == HabitFrequencyType.TimesPerWeek);
    }
}