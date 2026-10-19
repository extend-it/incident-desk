using System.Text.Json;
using Azure.Messaging.ServiceBus;

// Reads incidents from the "incidents-in" Service Bus queue and stores them in two steps,
// so there is a window where an incident exists but is not finished yet:
//   1. valid message   -> stored as "Processing"
//   2. after Enrichment:DelaySeconds -> updated to "Ready", message completed
// Invalid messages are dead-lettered with reason "ValidationFailed".
// If Service Bus is unreachable, errors are logged and the HTTP endpoints keep working.
public class IncidentQueueWorker(
    IncidentStore store,
    IConfiguration configuration,
    ILogger<IncidentQueueWorker> logger) : BackgroundService
{
    public const string QueueName = "incidents-in";

    // Local Service Bus emulator (docker/docker-compose.yaml); SAS_KEY_VALUE is the emulator's fixed key.
    private const string EmulatorConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    // Same rules as ASP.NET Core's request binding: camelCase, case-insensitive property names.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TimeSpan _enrichmentDelay =
        TimeSpan.FromSeconds(configuration.GetValue("Enrichment:DelaySeconds", 4));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Don't hold up app startup while we connect.
        await Task.Yield();

        var connectionString = configuration["ServiceBus:ConnectionString"] ?? EmulatorConnectionString;

        ServiceBusClient? client = null;
        ServiceBusProcessor? processor = null;
        try
        {
            client = new ServiceBusClient(connectionString);
            processor = client.CreateProcessor(QueueName, new ServiceBusProcessorOptions
            {
                // We complete or dead-letter each message ourselves.
                AutoCompleteMessages = false,
                MaxConcurrentCalls = 1,
            });
            processor.ProcessMessageAsync += HandleMessageAsync;
            processor.ProcessErrorAsync += HandleErrorAsync;

            await processor.StartProcessingAsync(stoppingToken);
            // StartProcessingAsync doesn't connect yet; connection failures arrive later in HandleErrorAsync.
            logger.LogInformation("Started Service Bus processor for queue '{Queue}'.", QueueName);

            // Keep running until the app shuts down.
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            // Never let this crash the host: the HTTP API must keep working without Service Bus.
            logger.LogError(ex,
                "Could not start the Service Bus listener for queue '{Queue}'. Queued incidents will NOT be processed; " +
                "the HTTP API keeps working. Is the emulator running? (docker compose -f docker/docker-compose.yaml up -d)",
                QueueName);
        }
        finally
        {
            if (processor is not null)
            {
                try { await processor.StopProcessingAsync(CancellationToken.None); } catch { /* shutting down anyway */ }
                await processor.DisposeAsync();
            }
            if (client is not null) await client.DisposeAsync();
        }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        CreateIncidentRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<CreateIncidentRequest>(args.Message.Body.ToString(), JsonOptions);
        }
        catch (JsonException ex)
        {
            await DeadLetterAsync(args, $"Message body is not valid JSON: {ex.Message}");
            return;
        }

        // A body of "null" deserializes to null; treat it like an empty object so every field is reported missing.
        var errors = (request ?? new CreateIncidentRequest(null, null, null)).Validate();
        if (errors.Count > 0)
        {
            var description = string.Join("; ", errors.Select(e => $"{e.Key}: {string.Join(" ", e.Value)}"));
            await DeadLetterAsync(args, description);
            return;
        }

        var incident = new Incident(
            Guid.NewGuid(),
            request!.TenantId!,
            request.Title!,
            request.Severity!,
            DateTimeOffset.UtcNow,
            IncidentStatus.Processing);
        store.Add(incident);
        logger.LogInformation("Incident {Id} from message {MessageId} stored as Processing.", incident.Id, args.Message.MessageId);

        // Simulated enrichment step. The processor renews the message lock meanwhile.
        await Task.Delay(_enrichmentDelay, args.CancellationToken);

        store.Update(incident with { Status = IncidentStatus.Ready });
        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        logger.LogInformation("Incident {Id} is Ready; message {MessageId} completed.", incident.Id, args.Message.MessageId);
    }

    private async Task DeadLetterAsync(ProcessMessageEventArgs args, string description)
    {
        logger.LogWarning("Dead-lettering message {MessageId}: {Description}", args.Message.MessageId, description);
        await args.DeadLetterMessageAsync(args.Message, "ValidationFailed", description, args.CancellationToken);
    }

    // Called for connection failures (e.g. emulator not running) and for exceptions thrown by HandleMessageAsync.
    // The processor keeps retrying on its own, so this only logs.
    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception,
            "Service Bus error on '{Entity}' ({Source}). Is the emulator running? " +
            "(docker compose -f docker/docker-compose.yaml up -d) The HTTP API keeps working.",
            args.EntityPath, args.ErrorSource);
        return Task.CompletedTask;
    }
}
