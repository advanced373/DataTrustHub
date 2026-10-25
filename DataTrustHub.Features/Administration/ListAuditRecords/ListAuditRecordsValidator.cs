using FluentValidation;

namespace DataTrustHub.Features.Administration.ListAuditRecords;

internal class ListAuditRecordsValidator : AbstractValidator<ListAuditRecordsQuery>
{
    public ListAuditRecordsValidator()
    {
        RuleFor(x => x.Action).MaximumLength(Constants.ActionMaxLength);

        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .When(x => x.From.HasValue && x.To.HasValue);
    }
}
