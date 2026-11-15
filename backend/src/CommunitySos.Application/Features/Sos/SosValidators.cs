using CommunitySos.Application.Common;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using FluentValidation;

namespace CommunitySos.Application.Features.Sos;

internal sealed class CreateSosRequestValidator : AbstractValidator<CreateSosRequest>
{
    public CreateSosRequestValidator()
    {
        RuleFor(x => x.ClientRequestId).NotEmpty();
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Description).MaximumLength(SosRequest.DescriptionMaxLength);
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.AccuracyMeters).GreaterThanOrEqualTo(0).When(x => x.AccuracyMeters.HasValue);
        RuleFor(x => x.CapturedAt).NotEmpty();
    }
}

internal sealed class CancelSosRequestValidator : AbstractValidator<CancelSosRequest>
{
    public CancelSosRequestValidator() => RuleFor(x => x.Reason).MaximumLength(SosRequest.RemarksMaxLength);
}

internal sealed class ResolveSosRequestValidator : AbstractValidator<ResolveSosRequest>
{
    public ResolveSosRequestValidator() => RuleFor(x => x.Remarks).MaximumLength(SosRequest.RemarksMaxLength);
}

internal sealed class SosListQueryValidator : AbstractValidator<SosListQuery>
{
    public SosListQueryValidator()
    {
        this.AddPagingRules();
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
    }
}

internal sealed class AlertListQueryValidator : AbstractValidator<AlertListQuery>
{
    public AlertListQueryValidator() => this.AddPagingRules();
}

internal sealed class RespondToSosRequestValidator : AbstractValidator<RespondToSosRequest>
{
    public RespondToSosRequestValidator()
    {
        RuleFor(x => x.Response)
            .IsInEnum()
            .NotEqual(ResponseType.None)
            .WithMessage("Choose CanHelp, OnTheWay, Arrived or CannotHelp.");
        RuleFor(x => x.Note).MaximumLength(SosNotification.NoteMaxLength);
    }
}

internal sealed class IssueLocationKeyRequestValidator : AbstractValidator<IssueLocationKeyRequest>
{
    public IssueLocationKeyRequestValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty().MaximumLength(Domain.Entities.LocationKey.DeviceIdMaxLength);
    }
}

internal sealed class RevokeLocationKeyRequestValidator : AbstractValidator<RevokeLocationKeyRequest>
{
    public RevokeLocationKeyRequestValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty().MaximumLength(Domain.Entities.LocationKey.DeviceIdMaxLength);
    }
}

internal sealed class UpdateLocationRequestValidator : AbstractValidator<UpdateLocationRequest>
{
    public UpdateLocationRequestValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.AccuracyMeters).InclusiveBetween(0, 100_000).When(x => x.AccuracyMeters.HasValue);
        RuleFor(x => x.CapturedAt).NotEmpty();
    }
}
