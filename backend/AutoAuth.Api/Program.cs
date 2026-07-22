using AutoAuth.Api.Models;
using AutoAuth.Api.Services;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<PrototypeStore>();
builder.Services.AddSingleton<RulesEvaluator>();
builder.Services.AddSingleton<ObjectiveGuidelineService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalReact", policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                {
                    return false;
                }

                var isLocalHost = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);

                return isLocalHost && uri.Port is >= 5173 and <= 5179;
            })
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();
var frontendDist = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "../../frontend/dist"));

if (Directory.Exists(frontendDist))
{
    var frontendFiles = new PhysicalFileProvider(frontendDist);

    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = frontendFiles
    });

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = frontendFiles
    });
}

app.UseCors("LocalReact");

if (!Directory.Exists(frontendDist))
{
    app.MapGet("/", () => Results.Redirect("/api/prototype"));
}

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", app = "AutoAuth Rules Engine Prototype" }));

app.MapPost("/api/shutdown", (ShutdownRequest request, IHostApplicationLifetime lifetime) =>
{
    if (!request.Confirm)
    {
        return Results.BadRequest(new { message = "Shutdown requires confirm=true." });
    }

    _ = Task.Run(async () =>
    {
        await Task.Delay(500);
        lifetime.StopApplication();
    });

    return Results.Ok(new { status = "shutting_down" });
});

app.MapGet("/api/prototype", (PrototypeStore store) => Results.Ok(store.Snapshot()));

app.MapGet("/api/objective-guidelines", (string? metricMode, ObjectiveGuidelineService guidelines) => Results.Ok(guidelines.Summaries(metricMode)));

app.MapGet("/api/objective-guidelines/precision-preview", (
    decimal? precisionThreshold,
    bool? useConfidenceThreshold,
    decimal? confidenceThreshold,
    bool? useSynapseUtilizationRateFilter,
    string? utilizationReferenceSource,
    decimal? synapseUtilizationDelta,
    string? metricMode,
    ObjectiveGuidelineService guidelines) =>
{
    return Results.Ok(guidelines.PrecisionPreview(
        precisionThreshold ?? 90m,
        useConfidenceThreshold ?? false,
        confidenceThreshold ?? 90m,
        useSynapseUtilizationRateFilter ?? false,
        utilizationReferenceSource,
        synapseUtilizationDelta ?? 0m,
        metricMode));
});

app.MapGet("/api/objective-guidelines/{hsim}", (string hsim, string? metricMode, ObjectiveGuidelineService guidelines) =>
{
    try
    {
        return Results.Ok(guidelines.Detail(hsim, metricMode));
    }
    catch (InvalidOperationException exception)
    {
        return Results.NotFound(new { message = exception.Message });
    }
});

app.MapGet("/api/dashboard", (PrototypeStore store) => Results.Ok(store.Dashboard()));

app.MapGet("/api/rules", (PrototypeStore store) => Results.Ok(store.Rules.OrderBy(rule => rule.Priority)));

app.MapPut("/api/rules/{id}", (string id, RuleUpdateRequest update, PrototypeStore store) =>
{
    try
    {
        return Results.Ok(store.UpdateRule(id, update));
    }
    catch (InvalidOperationException exception)
    {
        return Results.NotFound(new { message = exception.Message });
    }
});

app.MapGet("/api/authorization-requests", (PrototypeStore store) => Results.Ok(store.Requests));

app.MapGet("/api/evaluations", (PrototypeStore store) => Results.Ok(store.Evaluations.OrderByDescending(evaluation => evaluation.EvaluatedAt)));

app.MapPost("/api/evaluate", (EvaluationRequest request, RulesEvaluator evaluator) =>
{
    try
    {
        return Results.Ok(evaluator.Evaluate(request.RequestId));
    }
    catch (InvalidOperationException exception)
    {
        return Results.NotFound(new { message = exception.Message });
    }
});

if (Directory.Exists(frontendDist))
{
    app.MapFallback(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(Path.Combine(frontendDist, "index.html"));
    });
}

app.Run();
