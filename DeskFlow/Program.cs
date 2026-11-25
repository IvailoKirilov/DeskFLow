using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using DeskFlow;

SQLitePCL.Batteries_V2.Init();
var builder = WebApplication.CreateBuilder(args);
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("bg-BG");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("bg-BG");

builder.Services.AddRazorPages();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".DeskFlow.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.IdleTimeout = TimeSpan.FromHours(8);
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<OfficeClock>();
builder.Services.AddSingleton<OfficeStore>();
builder.Services.AddHostedService<NoShowWorker>();

var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error");
app.Services.GetRequiredService<OfficeStore>().Initialize();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.MapRazorPages();
app.Run();
