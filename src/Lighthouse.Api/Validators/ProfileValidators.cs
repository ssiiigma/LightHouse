using FluentValidation;
using Lighthouse.Shared.Contracts.Notifications;
using Lighthouse.Shared.Contracts.Profile;

namespace Lighthouse.Api.Validators;

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
}

public sealed class NotificationSettingsDtoValidator : AbstractValidator<NotificationSettingsDto>
{
    public NotificationSettingsDtoValidator()
    {
        // Тихий режим задається парою значень: початок і кінець.
        RuleFor(x => x.QuietHoursEnd).NotNull().When(x => x.QuietHoursStart is not null)
            .WithMessage("Quiet hours need both a start and an end.");
        RuleFor(x => x.QuietHoursStart).NotNull().When(x => x.QuietHoursEnd is not null)
            .WithMessage("Quiet hours need both a start and an end.");
    }
}

public sealed class RegisterDeviceRequestValidator : AbstractValidator<RegisterDeviceRequest>
{
    public RegisterDeviceRequestValidator() => RuleFor(x => x.FcmToken).NotEmpty().MaximumLength(512);
}