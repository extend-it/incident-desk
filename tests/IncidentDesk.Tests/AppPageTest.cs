using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace IncidentDesk.Tests;

public abstract class AppPageTest : PageTest
{
    protected IAPIRequestContext Api = null!;
    protected ILocator Incidents => Page.GetByRole(AriaRole.List, new() { Name = "Incidents" })
                            .GetByRole(AriaRole.Listitem);

    protected static string Setting(string name, string fallback) =>
        TestContext.Parameters.Get(name)
        ?? Environment.GetEnvironmentVariable(name)
        ?? fallback;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = Setting("WEB_BASE_URL", "http://localhost:5173"),
    };

    [SetUp]
    public async Task CreateApiContext() =>
        Api = await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = Setting("API_BASE_URL", "http://localhost:5080"),
        });

    [TearDown]
    public async Task DisposeApiContext() => await Api.DisposeAsync();

    /// <summary>Arrange through the API: create an incident and return its title.</summary>
    protected async Task<string> SeedIncidentAsync(string tenantId, string titlePrefix, string severity = "High")
    {
        var title = $"{titlePrefix} {Guid.NewGuid():N}";
        var response = await Api.PostAsync("/api/incidents", new()
        {
            DataObject = new { tenantId, title, severity },
        });
        await Expect(response).ToBeOKAsync();   // any 2xx, so 201 Created passes
        return title;
    }
}