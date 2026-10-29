using FluentValidation;

namespace DataTrustHub.Features.Administration.GetOrganization;

internal class GetOrganizationValidator : AbstractValidator<GetOrganizationQuery>
{
    public GetOrganizationValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
    }
}
