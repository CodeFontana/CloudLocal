using BlazorUI.Configuration;
using BlazorUI.Features;
using BlazorUI.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        options.HandshakeTimeout = TimeSpan.FromSeconds(30);
    });
builder.Services.AddResponseCompression();
builder.Services.AddHttpContextAccessor();
builder.Services.AddJSComponents();

CloudLocalOptions cloudLocal = builder.Configuration.GetSection(CloudLocalOptions.SectionName).Get<CloudLocalOptions>() ?? new CloudLocalOptions();
builder.Services.AddCloudLocal(cloudLocal, builder.Environment.ContentRootPath);

WebApplication app = builder.Build();

app.UseExceptionHandler("/Error", createScopeForErrors: true);

if (app.Environment.IsDevelopment() == false)
{
    app.UseHsts();
    app.UseResponseCompression();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AllowAnonymous();
app.Run();
