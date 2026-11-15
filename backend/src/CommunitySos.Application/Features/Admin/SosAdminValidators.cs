using CommunitySos.Application.Common;
using CommunitySos.Domain.Entities;
using FluentValidation;

namespace CommunitySos.Application.Features.Admin;

internal sealed class AdminSosListQueryValidator : AbstractValidator<AdminSosListQuery>
{
    public AdminSosListQueryValidator()
    {
        this.AddPagingRules();
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.To).GreaterThan(x => x.From).When(x => x.From.HasValue && x.To.HasValue);
    }
}

internal sealed class ApproveSosRequestValidator : AbstractValidator<ApproveSosRequest>
{
    public ApproveSosRequestValidator() => RuleFor(x => x.Remarks).MaximumLength(SosRequest.RemarksMaxLength);
}

internal sealed class RejectSosRequestValidator : AbstractValidator<RejectSosRequest>
{
    public RejectSosRequestValidator() =>
        RuleFor(x => x.Remarks)
            .NotEmpty().WithMessage("Remarks are required when rejecting an SOS.")
            .MaximumLength(SosRequest.RemarksMaxLength);
}

internal sealed class DashboardQueryValidator : AbstractValidator<DashboardQuery>
{
    public DashboardQueryValidator() =>
        RuleFor(x => x.To).GreaterThan(x => x.From).When(x => x.From.HasValue && x.To.HasValue);
}

internal sealed class SosMapQueryValidator : AbstractValidator<SosMapQuery>
{
    public SosMapQueryValidator()
    {
        RuleFor(x => x.MinLatitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.MaxLatitude).InclusiveBetween(-90, 90).GreaterThan(x => x.MinLatitude);
        RuleFor(x => x.MinLongitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.MaxLongitude).InclusiveBetween(-180, 180).GreaterThan(x => x.MinLongitude);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.To).GreaterThan(x => x.From).When(x => x.From.HasValue && x.To.HasValue);
    }
}
