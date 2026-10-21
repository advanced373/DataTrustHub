using FluentValidation;

namespace DataTrustHub.Features.DataManagement.ListClassificationOptions;

internal class ListClassificationOptionsValidator : AbstractValidator<ListClassificationOptionsQuery>
{
    public ListClassificationOptionsValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}
