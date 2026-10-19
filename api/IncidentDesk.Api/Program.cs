using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// Turns unhandled errors (e.g. malformed JSON bodies) into problem-details responses.
builder.Services.AddProblemDetails();

// One in-memory store shared by all requests; data is lost when the app stops.
builder.Services.AddSingleton<IncidentStore>();

// Consumes the "incidents-in" Service Bus queue; see IncidentQueueWorker.cs.
builder.Services.AddHostedService<IncidentQueueWorker>();

var app = builder.Build();

// A malformed or missing JSON body throws BadHttpRequestException; keep its 400 instead of a 500.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError
});
app.UseStatusCodePages();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// GET /api/incidents?tenantId=contoso
app.MapGet("/api/incidents", (string? tenantId, IncidentStore store) =>
{
    if (string.IsNullOrWhiteSpace(tenantId))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["tenantId"] = ["The tenantId query parameter is required."]
        });
    }

    return Results.Ok(store.GetForTenant(tenantId));
});

// POST /api/incidents  { "tenantId": "...", "title": "...", "severity": "High" }
app.MapPost("/api/incidents", (CreateIncidentRequest request, IncidentStore store) =>
{
    var errors = request.Validate();
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    // Created directly over HTTP, so there is nothing to wait for: "Ready" straight away.
    var incident = new Incident(
        Guid.NewGuid(),
        request.TenantId!,
        request.Title!,
        request.Severity!,
        DateTimeOffset.UtcNow,
        IncidentStatus.Ready);

    store.Add(incident);

    // There is no GET-by-id endpoint, so no Location header is set.
    return Results.Created((string?)null, incident);
});

app.Run();

public record Incident(Guid Id, string TenantId, string Title, string Severity, DateTimeOffset CreatedAt, string Status)
{
    // Case-sensitive: "High" is valid, "high" is not.
    public static readonly string[] Severities = ["Low", "Medium", "High", "Critical"];
}

// "Processing" while a queued incident is being enriched, "Ready" once it is complete.
public static class IncidentStatus
{
    public const string Processing = "Processing";
    public const string Ready = "Ready";
}

// Fields are nullable so missing values reach our validation instead of failing JSON binding.
// Used both for the POST body and for "incidents-in" queue messages.
public record CreateIncidentRequest(string? TenantId, string? Title, string? Severity)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(TenantId))
            errors["tenantId"] = ["The tenantId field is required."];

        if (string.IsNullOrWhiteSpace(Title))
            errors["title"] = ["The title field is required."];

        if (string.IsNullOrWhiteSpace(Severity))
            errors["severity"] = ["The severity field is required."];
        else if (!Incident.Severities.Contains(Severity))
            errors["severity"] = [$"Severity must be one of: {string.Join(", ", Incident.Severities)}."];

        return errors;
    }
}

public class IncidentStore
{
    private readonly ConcurrentDictionary<Guid, Incident> _incidents = new();

    public void Add(Incident incident) => _incidents[incident.Id] = incident;

    // Replaces a stored incident (e.g. "Processing" -> "Ready"); same as Add, named for intent.
    public void Update(Incident incident) => _incidents[incident.Id] = incident;

    public IReadOnlyList<Incident> GetForTenant(string tenantId) =>
        _incidents.Values
            .Where(i => i.TenantId == tenantId)
            
            // Mutation Test 1: tenantId filter removed => the API stops filtering by tenant
            // .Where(i => true)   
            // Expected Results:
            // - IncidentOfAnotherTenantIsNotListed fails at line 52: count 1, expected 0.
            // - EachTenantSeesItsOwnIncident still passes. It only checks that each tenant sees its own incident, 
            //      never that it doesn’t see the other’s. A leak between tenants gets past it. 
            //      That’s why the absence test exists, and it’s a good point to demonstrate that : 
            //          presence tests can’t catch data leaks.

            .OrderByDescending(i => i.CreatedAt)
            .ToList();
}
