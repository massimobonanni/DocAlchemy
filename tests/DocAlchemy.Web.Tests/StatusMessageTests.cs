using DocAlchemy.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DocAlchemy.Web.Tests;

public class StatusMessageTests
{
    [Fact]
    public async Task ErrorIsAnnouncedAssertivelyAndMessageIsEncoded()
    {
        var markup = await RenderStatusMessageAsync(
            StatusMessageKind.Error,
            "<script>unsafe()</script>");

        Assert.Contains("role=\"alert\"", markup, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"assertive\"", markup, StringComparison.Ordinal);
        Assert.Contains("Error", markup, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;unsafe()&lt;/script&gt;", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WarningHasVisibleNonColorLabelAndPoliteAnnouncement()
    {
        var markup = await RenderStatusMessageAsync(
            StatusMessageKind.Warning,
            "Check the extracted value.");

        Assert.Contains("status-message--warning", markup, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", markup, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", markup, StringComparison.Ordinal);
        Assert.Contains("Review required", markup, StringComparison.Ordinal);
    }

    private static async Task<string> RenderStatusMessageAsync(
        StatusMessageKind kind,
        string message)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>());

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<StatusMessage>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(StatusMessage.Kind)] = kind,
                    [nameof(StatusMessage.Message)] = message
                }));

            return output.ToHtmlString();
        });
    }
}
