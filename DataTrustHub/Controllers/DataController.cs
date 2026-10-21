using DataTrustHub.Models;
using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace DataTrustHub.Controllers;

public class DataController(IDataApiClient dataApi, IUserSessionService userSession)
    : ApiControllerBase(userSession)
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        await ListViewAsync(await dataApi.ListAsync(cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var result = await dataApi.GetContentAsync(id, cancellationToken);
        if (!result.IsSuccess) return await RedirectWithOutcomeAsync(result, string.Empty, nameof(Index), ControllerNames.Data);

        var bytes = DecodeContent(result.Value!.Content);
        if (bytes is null) return await RedirectWithOutcomeAsync(
            ApiResult<bool>.Failure(Messages.DownloadFailed), string.Empty, nameof(Index), ControllerNames.Data);

        return File(bytes, ResolveContentType(result.Value.Name), result.Value.Name);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await dataApi.DeleteAsync(id, cancellationToken);
        return await RedirectWithOutcomeAsync(result, Messages.FileDeleted, nameof(Index), ControllerNames.Data);
    }

    [HttpGet]
    public IActionResult Upload() => View(new UploadDataViewModel());

    [HttpPost]
    // The request limit leaves room for multipart overhead, so a file just over the size limit
    // reaches the friendly validation message below instead of a bare 413 response
    [RequestSizeLimit(Constants.MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = Constants.MaxUploadRequestBytes)]
    public async Task<IActionResult> Upload(UploadDataViewModel model, CancellationToken cancellationToken)
    {
        if (model.File?.Length > Constants.MaxUploadSizeBytes)
            ModelState.AddModelError(nameof(model.File), Messages.FileTooLarge);

        if (!ModelState.IsValid) return View(model);

        var result = await dataApi.UploadAsync(model.File!, cancellationToken);
        if (!result.IsSuccess) return await HandleFailureAsync(result, model);

        var message = string.Format(Messages.FileUploaded, model.File!.FileName);
        return RedirectWithSuccess(message, nameof(Index), ControllerNames.Data);
    }

    // The API stores file content base64-encoded
    private static byte[]? DecodeContent(string? content)
    {
        if (content is null) return null;

        var buffer = new byte[content.Length];
        return Convert.TryFromBase64String(content, buffer, out var length) ? buffer[..length] : null;
    }

    private static string ResolveContentType(string fileName) =>
        ContentTypes.TryGetContentType(fileName, out var contentType) ? contentType : Constants.DefaultContentType;
}
