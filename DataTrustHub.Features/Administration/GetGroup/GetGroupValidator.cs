using FluentValidation;

namespace DataTrustHub.Features.Administration.GetGroup;

internal class GetGroupValidator : AbstractValidator<GetGroupQuery>
{
    public GetGroupValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();
    }
}
