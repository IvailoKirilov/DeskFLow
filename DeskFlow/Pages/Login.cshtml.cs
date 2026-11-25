using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DeskFlow.Pages;

[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class LoginModel(OfficeStore store) : PageModel
{
    public const string SessionKey = "EmployeeID";
    [BindProperty] public string? Code { get; set; }
    public string? Error { get; private set; }

    public IActionResult OnGet()
    {
        var code = HttpContext.Session.GetString(SessionKey);
        return code is not null && store.FindEmployee(code) is not null ? RedirectToPage("/Index") : Page();
    }

    public IActionResult OnPost()
    {
        var code = Code?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code) || code.Length > 24 || store.FindEmployee(code) is null)
        {
            Error = "Несъществуващ потребителски код. Моля, опитайте отново.";
            return Page();
        }

        HttpContext.Session.SetString(SessionKey, code);
        return RedirectToPage("/Index");
    }
}
