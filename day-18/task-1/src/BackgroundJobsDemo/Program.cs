using System.Text.Json.Serialization;
using BackgroundJobsDemo.Jobs;
using BackgroundJobsDemo.Queue;
using BackgroundJobsDemo.Worker;

var builder = WebApplication.CreateBuilder(args);

// Bounded to 3: this demo holds transient, non-persisted work behind a single worker. A small
// bound makes backpressure something you can actually trigger and test within seconds, and
// keeps memory bounded if producers ever outpace the worker. A real system would size this from
// measured throughput, not a demo default.
const int QueueCapacity = 3;

builder.Services.AddSingleton<IBackgroundTaskQueue>(_ => new ChannelBackgroundTaskQueue(QueueCapacity));
builder.Services.AddSingleton<IJobStatusStore, InMemoryJobStatusStore>();
builder.Services.AddHostedService<QueuedHostedService>();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The framework default (5s) is tight once the web server is also draining in-flight HTTP
// requests during the same window. 10s is a safety margin for a clean stop -- not a promise to
// finish queued work; see README, "Graceful shutdown: what happens to queued work", for why
// still-queued and in-flight jobs are deliberately abandoned on shutdown here.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(10));

var app = builder.Build();

app.MapJobsEndpoints();

app.Run();

public partial class Program;
