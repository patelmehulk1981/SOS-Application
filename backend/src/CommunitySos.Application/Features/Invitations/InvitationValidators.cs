using CommunitySos.Application.Common;
using CommunitySos.Domain.Entities;
using FluentValidation;

namespace CommunitySos.Application.Features.Invitations;

internal sealed class CreateInvitationRequestValidator : AbstractValidator<CreateInvitationRequest>
{
    public CreateInvitationRequestValidator(TimeProvider clock)
    {
        RuleFor(x => x.Label).MaximumLength(Invitation.LabelMaxLength);
        RuleFor(x => x.MaxUses).InclusiveBetween(1, Invitation.MaxUsesLimit).When(x => x.MaxUses.HasValue);
        RuleFor(x => x.ExpiresAt)
            .Must(e => e!.Value.ToUniversalTime() > clock.GetUtcNow().UtcDateTime)
            .WithMessage("Expiry must be in the future.")
            .Must(e => e!.Value.ToUniversalTime() <= clock.GetUtcNow().UtcDateTime + Invitation.MaxLifetime)
            .WithMessage("Expiry can be at most one year ahead.")
            .When(x => x.ExpiresAt.HasValue);
    }
}

internal sealed class InvitationListQueryValidator : AbstractValidator<InvitationListQuery>
{
    public InvitationListQueryValidator()
    {
        this.AddPagingRules();
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
    }
}
