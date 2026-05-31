using FluentValidation;

namespace DataTrustHub.Features.DataManagement.AddDataItem;

internal class AddDataItemValidator : AbstractValidator<AddDataItemCommand>
{
    public AddDataItemValidator()
    {
        RuleFor(x => x.File)
            .NotNull()
            .WithMessage("A file must be provided.")
            .Must(f => f is null || f.Length > 0)
            .WithMessage("The uploaded file must not be empty.")
            .Must(f => f is null || f.Length <= Constants.MaxFileSizeBytes)
            .WithMessage($"File exceeds the maximum allowed size.");

        RuleFor(x => x.File!.FileName)
            .NotEmpty()
            .MaximumLength(Constants.MaxFileNameLength)
            .When(x => x.File is not null);

        RuleFor(x => x.OwnerUserId)
            .NotEmpty();
    }
}
