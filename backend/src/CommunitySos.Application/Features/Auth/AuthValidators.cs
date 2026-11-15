using CommunitySos.Domain.Entities;
using CommunitySos.Domain.ValueObjects;
using FluentValidation;

namespace CommunitySos.Application.Features.Auth;

internal sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.FirebaseIdToken).NotEmpty().MaximumLength(4096);

        RuleFor(x => x.InviteCode)
            .Must(InvitationCode.IsWellFormed)
            .When(x => !string.IsNullOrWhiteSpace(x.InviteCode))
            .WithMessage("Invitation code is not valid.");

        RuleFor(x => x.Consent!.PolicyVersion)
            .NotEmpty()
            .MaximumLength(UserConsent.PolicyVersionMaxLength)
            .When(x => x.Consent is not null);

        RuleFor(x => x.DeviceId).MaximumLength(RefreshToken.DeviceIdMaxLength);
    }
}

internal sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DeviceId).MaximumLength(RefreshToken.DeviceIdMaxLength);
    }
}

internal sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FcmToken).MaximumLength(UserDevice.FcmTokenMaxLength);
        RuleFor(x => x.InstallationId).MaximumLength(UserDevice.InstallationIdMaxLength);
    }
}
