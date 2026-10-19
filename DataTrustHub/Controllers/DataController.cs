using DataTrustHub.Models;
using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.Controllers;

public class DataController(IDataApiClient dataApi, IUserSessionService userSession)
    : ApiControllerBase(userSession)
{
    // Listing data needs GET /data from the API (issue #5); the view shows a placeholder until then
    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public IActionResult Upload() => View(new UploadDataViewModel());

    [HttpPost]
    [RequestSizeLimit(Constants.MaxUploadSizeBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = Constants.MaxUploadSizeBytes)]
    public async Task<IActionResult> Upload(UploadDataViewModel model, CancellationToken cancellationToken)
    {
        if (model.File?.Length > Constants.MaxUploadSizeBytes)
            ModelState.AddModelError(nameof(model.File), Messages.FileTooLarge);

        if (!ModelState.IsValid) return View(model);

        var result = await dataApi.UploadAsync(model.File!, cancellationToken);
        if (!result.IsSuccess) return await HandleFailureAsync(result, model);

        var message = string.Format(Messages.FileUploaded, result.Value);
        return RedirectWithSuccess(message, nameof(Index), ControllerNames.Data);
    }
}
