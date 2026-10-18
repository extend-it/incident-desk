using Microsoft.Playwright;

namespace IncidentDesk.Tests;

[TestFixture]
public class IncidentListStateTests  : AppPageTest
{
    private ILocator Incidents => Page.GetByRole(AriaRole.List, new() { Name = "Incidents" })
                                .GetByRole(AriaRole.Listitem);
    private const string IncidentApi = "**/api/incidents*";

    [Test]
    public async Task EmptyListForStatus200ShowsTheNoIncidentsMessage()
    {
        await Page.RouteAsync(IncidentApi, route => route.FulfillAsync(new()
        {
            Status = 200, ContentType = "application/json", Body = "[]",
        }));

        await Page.GotoAsync("/?tenant=contoso");

        await Expect(Page.GetByText("No incidents for contoso.")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
    }
    
    [Test]
    public async Task ServerError500ShowsAnErrorInsteadOfAnEmptyList()
    {
        await Page.RouteAsync(IncidentApi, route => route.FulfillAsync(new() { Status = 500 }));

        await Page.GotoAsync("/?tenant=contoso");

        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Couldn't load incidents (HTTP 500)");
        await Expect(Page.GetByText("No incidents for contoso.")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task ServerError503ShowsAnErrorInsteadOfAnEmptyList()
    {
        await Page.RouteAsync(IncidentApi, route => route.FulfillAsync(new() { Status = 503 }));

        await Page.GotoAsync("/?tenant=contoso");

        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Couldn't load incidents (HTTP 503)");
        await Expect(Page.GetByText("No incidents for contoso.")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task UnauthorizedAccessError403ShowsAnErrorInsteadOfAnEmptyList()
    {
        await Page.RouteAsync(IncidentApi, route => route.FulfillAsync(new() { Status = 403 }));

        await Page.GotoAsync("/?tenant=contoso");

        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("You don't have access to this tenant's incidents.");
        await Expect(Page.GetByText("No incidents for contoso.")).ToHaveCountAsync(0);
    }

    [Test]
    // Instead of fulfilling, call route.AbortAsync(). The request then fails like a dropped connection. 
    // The alert should read exactly “Couldn't load incidents”, with no “(HTTP …)”.
    public async Task NetworkFailureShowsAnErrorWithoutStatus()
    {
        await Page.RouteAsync(IncidentApi, route => route.AbortAsync());

        await Page.GotoAsync("/?tenant=contoso");

        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Couldn't load incidents");
        await Expect(Page.GetByText("No incidents for contoso.")).ToHaveCountAsync(0);
    }

    [Test]
    // This test uses inside the route handler, await Task.Delay(2000) before FulfillAsync with a one-incident JSON body. 
    // Assert that “Loading incidents…” (AriaRole.Status) is visible, then that the incident is visible, then that the loading message is gone (count 0)
    public async Task SlowResponseShowsLoadingFirstThenTheList()
    {
        await Page.RouteAsync(IncidentApi, async route => 
        {
            await Task.Delay(2000);
            await route.FulfillAsync(new()
            {
                Status = 200, ContentType = "application/json", 
                Body = """[{"id":"00000000-0000-0000-0000-000000000001","tenantId":"contoso","title":"Slow incident","severity":"Low","createdAt":"2026-10-18T10:00:00Z"}]""",
            });
        });
        await Page.GotoAsync("/?tenant=contoso");

        var loading = Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Loading incidents…"});
        await Expect(loading).ToBeVisibleAsync();
        await Expect(Incidents.Filter(new() { HasText = "Slow incident" })).ToBeVisibleAsync();
        await Expect(loading).ToHaveCountAsync(0);
    }
}