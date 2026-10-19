using Microsoft.Playwright;

namespace IncidentDesk.Tests;

[TestFixture]
public class IncidentListTests : AppPageTest
{
    [Test]
    public async Task IncidentCreatedThroughTheApiIsListedOnThePage()
    {
        var title = await SeedIncidentAsync("contoso", "Impossible travel");

        await Page.GotoAsync("/?tenant=contoso");

        var row = Incidents.Filter(new() { HasText = title });
        await Expect(row).ToBeVisibleAsync();
        await Expect(row).ToContainTextAsync("High");
    }

    [Test]
    public async Task EachTenantSeesItsOwnIncident()
    {
        var contosoTitle = await SeedIncidentAsync("contoso", "Incident for Contoso");
        var fabrikaTitle = await SeedIncidentAsync("fabrika", "Incident for Fabrika");

        await Page.GotoAsync("/?tenant=contoso");
        var rowInContoso = Incidents.Filter(new() { HasText = contosoTitle });
        await Expect(rowInContoso).ToBeVisibleAsync();
        await Expect(rowInContoso).ToContainTextAsync("High");
        
        await Page.GotoAsync("/?tenant=fabrika");
        var rowInFabrika = Incidents.Filter(new() { HasText = fabrikaTitle });
        await Expect(rowInFabrika).ToBeVisibleAsync();
        await Expect(rowInFabrika).ToContainTextAsync("High");
    }

    [Test]
    public async Task IncidentOfAnotherTenantIsNotListed()
    {
        var contosoTitle = await SeedIncidentAsync("contoso",  "Incident for Contoso");
        var fabrikaTitle = await SeedIncidentAsync("fabrika", "Incident for Fabrika");

        await Page.GotoAsync("/?tenant=contoso");

        // Anchor: wait until the list has actually rendered.
        await Expect(Incidents.Filter(new() { HasText = contosoTitle })).ToBeVisibleAsync();

        // Only now is "absent" meaningful.
        await Expect(Incidents.Filter(new() { HasText = fabrikaTitle })).ToHaveCountAsync(0);
    }
}
