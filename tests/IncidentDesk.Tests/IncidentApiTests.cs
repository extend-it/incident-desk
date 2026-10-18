using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace IncidentDesk.Tests;

[TestFixture]
public class IncidentApiTests : PlaywrightTest
{
    private IAPIRequestContext Api = null!;

    [SetUp]
    public async Task CreateApiContext() =>
    Api = await Playwright.APIRequest.NewContextAsync(new()
    {
        BaseURL = TestContext.Parameters.Get("API_BASE_URL", "http://localhost:5080"),
    });

    [TearDown]
    public async Task DisposeApiContext() => await Api.DisposeAsync();

    [Test]
    public async Task IncidentWithUnknownSeverityIsRejected()
    {
        var response = await Api.PostAsync("/api/incidents", new()
        {
            DataObject = new { tenantId = "contoso", title = "Unknown severity", severity = "Urgent"},
        });

        // Status first: if this fails, the body assertions below would be meaningless
        await Expect(response).Not.ToBeOKAsync();
        Assert.That(response.Status, Is.EqualTo(400));

        // Print the body once to see the problem-details format
        TestContext.Out.WriteLine(await response.TextAsync());
    }
}