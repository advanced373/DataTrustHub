using FluentValidation;

namespace DataTrustHub.Features.Administration.ListOrganizationAuditRecords;

internal class ListOrganizationAuditRecordsValidator : AbstractValidator<ListOrganizationAuditRecordsQuery>
{
    public ListOrganizationAuditRecordsValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.CallerId).NotEmpty();
        RuleFor(x => x.Action).MaximumLength(Constants.ActionMaxLength);

        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .When(x => x.From.HasValue && x.To.HasValue);
    }
}
