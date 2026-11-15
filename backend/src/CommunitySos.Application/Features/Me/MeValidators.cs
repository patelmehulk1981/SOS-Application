using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using FluentValidation;

namespace CommunitySos.Application.Features.Me;

internal sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    private static readonly string[] BloodGroups = ["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-"];

    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .Must(n => n.Trim().Length >= 2).WithMessage("Full name must be at least 2 characters.")
            .MaximumLength(User.FullNameMaxLength);

        RuleFor(x => x.Email)
            .EmailAddress()
            .MaximumLength(User.EmailMaxLength)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.BloodGroup)
            .Must(b => BloodGroups.Contains(b!.Trim().ToUpperInvariant()))
            .When(x => !string.IsNullOrWhiteSpace(x.BloodGroup))
            .WithMessage("Blood group must be one of A+, A-, B+, B-, AB+, AB-, O+, O-.");

        RuleFor(x => x.EmergencyContactName).MaximumLength(User.FullNameMaxLength);

        RuleFor(x => x.EmergencyContactPhone)
            .Matches(@"^[+\d][\d\s\-()]{7,19}$")
            .When(x => !string.IsNullOrWhiteSpace(x.EmergencyContactPhone))
            .WithMessage("Emergency contact phone is not valid.");
    }
}

internal sealed class UpdateConsentRequestValidator : AbstractValidator<UpdateConsentRequest>
{
    public UpdateConsentRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
    }
}

internal sealed class AcceptPolicyRequestValidator : AbstractValidator<AcceptPolicyRequest>
{
    public AcceptPolicyRequestValidator()
    {
        RuleFor(x => x.PolicyVersion).NotEmpty().MaximumLength(UserConsent.PolicyVersionMaxLength);
    }
}

internal sealed class RegisterDeviceRequestValidator : AbstractValidator<RegisterDeviceRequest>
{
    public RegisterDeviceRequestValidator()
    {
        RuleFor(x => x.InstallationId).MaximumLength(UserDevice.InstallationIdMaxLength);
        RuleFor(x => x.FcmToken).MaximumLength(UserDevice.FcmTokenMaxLength);
        RuleFor(x => x.InstallationId)
            .NotEmpty()
            .When(x => string.IsNullOrWhiteSpace(x.FcmToken))
            .WithMessage("Send installationId (or fcmToken from older app versions).");
        RuleFor(x => x.Platform).IsInEnum();
        RuleFor(x => x.AppVersion).MaximumLength(UserDevice.AppVersionMaxLength);
        RuleFor(x => x.DeviceModel).MaximumLength(UserDevice.DeviceModelMaxLength);
    }
}

internal sealed class UnregisterDeviceRequestValidator : AbstractValidator<UnregisterDeviceRequest>
{
    public UnregisterDeviceRequestValidator()
    {
        RuleFor(x => x.InstallationId).MaximumLength(UserDevice.InstallationIdMaxLength);
        RuleFor(x => x.FcmToken).MaximumLength(UserDevice.FcmTokenMaxLength);
        RuleFor(x => x.InstallationId)
            .NotEmpty()
            .When(x => string.IsNullOrWhiteSpace(x.FcmToken))
            .WithMessage("Send installationId or fcmToken.");
    }
}
