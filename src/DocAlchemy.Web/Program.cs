using System.Diagnostics;
using DocAlchemy.ContentUnderstanding;
using DocAlchemy.Storage;
using DocAlchemy.Web.Components;
using DocAlchemy.Web.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDocAlchemyConfiguration(builder.Configuration);
builder.Services.AddDocAlchemyContentUnderstanding(builder.Configuration);
builder.Services.AddDocAlchemyStorage(builder.Configuration);
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Logging.Configure(options =>
    options.ActivityTrackingOptions |= ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);

var applicationInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
{
    builder.Services.AddApplicationInsightsTelemetry(options =>
        options.ConnectionString = applicationInsightsConnectionString);
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
