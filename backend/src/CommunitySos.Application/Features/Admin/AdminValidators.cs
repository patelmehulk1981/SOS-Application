using CommunitySos.Application.Common;
using CommunitySos.Domain.Enums;
using FluentValidation;

namespace CommunitySos.Application.Features.Admin;

internal sealed class UserListQueryValidator : AbstractValidator<UserListQuery>
{
    public UserListQueryValidator()
    {
        this.AddPagingRules();
        RuleFor(x => x.Role).IsInEnum().When(x => x.Role.HasValue);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

internal sealed class ReferralListQueryValidator : AbstractValidator<ReferralListQuery>
{
    public ReferralListQueryValidator()
    {
        this.AddPagingRules();
        RuleFor(x => x.To).GreaterThan(x => x.From).When(x => x.From.HasValue && x.To.HasValue);
    }
}

internal sealed class UpdateUserStatusRequestValidator : AbstractValidator<UpdateUserStatusRequest>
{
    public const int ReasonMaxLength = 300;

    public UpdateUserStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .Must(s => s is UserStatus.Active or UserStatus.Suspended)
            .WithMessage("Status must be Active or Suspended.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required when suspending an account.")
            .When(x => x.Status == UserStatus.Suspended);

        RuleFor(x => x.Reason).MaximumLength(ReasonMaxLength);
    }
}
