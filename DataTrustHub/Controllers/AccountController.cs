using DataTrustHub.Models;
using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.Controllers;

public class AccountController(IAuthApiClient authApi, IUserSessionService userSession)
    : ApiControllerBase(userSession)
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToHome();

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await authApi.LoginAsync(model.Email, model.Password, cancellationToken);
        if (!result.IsSuccess) return await HandleFailureAsync(result, model);

        await UserSession.SignInAsync(result.Value!);
        return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl) : RedirectToHome();
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await authApi.RegisterAsync(model.Email, model.Password, cancellationToken);
        if (!result.IsSuccess) return await HandleFailureAsync(result, model);

        return RedirectWithSuccess(Messages.Registered, nameof(Login), ControllerNames.Account);
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await UserSession.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    private IActionResult RedirectToHome() =>
        RedirectToAction(nameof(HomeController.Index), ControllerNames.Home);
}
