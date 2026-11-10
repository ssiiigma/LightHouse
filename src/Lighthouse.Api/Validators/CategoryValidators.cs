using FluentValidation;
using Lighthouse.Shared.Contracts.Categories;

namespace Lighthouse.Api.Validators;

public sealed class UpsertCategoryRequestValidator : AbstractValidator<UpsertCategoryRequest>
{
    public UpsertCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ColorHex).Matches("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$").When(x => x.ColorHex is not null);
        RuleFor(x => x.Icon).MaximumLength(50);
    }
}