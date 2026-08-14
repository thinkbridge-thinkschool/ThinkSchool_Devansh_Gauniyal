using ResilienceDemo;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<RemoteService>();

builder.Services
    .AddHttpClient(HttpClientNames.RemoteService, client =>
    {
        client.BaseAddress = new Uri(
            builder.Configuration["RemoteService:BaseAddress"] ?? "https://example.invalid");
    })
    // Named resilience handler, built from three strategies added in this exact
    // order -- which matters. The first strategy added is the OUTERMOST wrapper and
    // the last is the INNERMOST, closest to the real HTTP call:
    //
    //   Retry (outermost) -> Circuit Breaker (middle) -> Timeout (innermost)
    //
    // That means: every retry attempt passes through the circuit breaker check
    // first (so an open circuit stops retries from even trying), and each
    // individual attempt gets its own timeout budget (so one slow attempt can't eat
    // the whole retry budget silently). Reordering this would change the meaning:
    // putting timeout outermost would cap the *entire* retry sequence instead of
    // each attempt, and putting circuit breaker outermost would mean a single
    // request's retries could each be evaluated as separate failures against the
    // breaker before retry ever got a chance to recover from a transient blip.
    .AddResilienceHandler(
        ResiliencePipelineConfiguration.PipelineName,
        ResiliencePipelineConfiguration.Configure);

var app = builder.Build();

app.MapGet("/call-remote", async (RemoteService remoteService, CancellationToken ct) =>
{
    try
    {
        var body = await remoteService.GetDataAsync(ct);
        return Results.Ok(new { data = body });
    }
    catch (Exception ex)
    {
        // A genuine failure -- retries and the circuit breaker already did what they
        // could. Surfaced here as a real error response, not swallowed into a fake
        // 200.
        return Results.Problem(
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

public partial class Program;
