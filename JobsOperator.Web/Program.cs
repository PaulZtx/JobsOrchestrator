using JobsOperator.Web.Components;
using JobsOperator.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var jobsApiAddress = builder.Configuration["JobsApi:BaseAddress"]
    ?? throw new InvalidOperationException("Jobs API address is not configured");

builder.Services.AddHttpClient<JobsApiClient>(client =>
{
    client.BaseAddress = new Uri(jobsApiAddress);
    client.Timeout = TimeSpan.FromMinutes(10);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (app.Urls.Any(url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
    app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
