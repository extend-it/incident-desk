using Microsoft.Playwright;
using Azure.Messaging.ServiceBus;

namespace IncidentDesk.Tests;

[TestFixture]
public class IncidentFromQueueTests : AppPageTest
{
    private ServiceBusClient _client = null!;

    [OneTimeSetUp]
    public void CreateClient() => _client = new ServiceBusClient(Setting(
        "SERVICEBUS_CONNECTION_STRING",
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"));

    [OneTimeTearDown]
    public async Task DisposeClient() => await _client.DisposeAsync();

    [Test]
    public async Task IncidentSentToTheQueueAppearsAsProcessingThenReady()
    {
        // Open the page first and wait until its first load is finished.
        await Page.GotoAsync("/?tenant=contoso");
        await Expect(Page.GetByRole(AriaRole.Status)).ToHaveCountAsync(0);   // "Loading incidents…" is gone

        var title = $"Malware detected {Guid.NewGuid():N}";
        await using (var sender = _client.CreateSender("incidents-in"))
        {
            await sender.SendMessageAsync(new ServiceBusMessage(
                BinaryData.FromObjectAsJson(new { tenantId = "contoso", title, severity = "Critical" })));
        }

        // The row can only appear through polling: the page is already loaded.
        var row = Incidents.Filter(new() { HasText = title });
        await Expect(row).ToContainTextAsync("Processing", new() { Timeout = 10_000 });
        await Expect(row).ToContainTextAsync("Ready", new() { Timeout = 15_000 });
    }
}