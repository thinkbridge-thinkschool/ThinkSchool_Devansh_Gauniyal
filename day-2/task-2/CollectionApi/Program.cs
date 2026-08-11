using CollectionApi.Data;
using CollectionApi.Repositories;
using CollectionApi.Services;
using CollectionApi.Services.Time;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<CollectionDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Collections")));
builder.Services.AddScoped<ICollectionRepository, CollectionRepository>();
builder.Services.AddScoped<ICollectionService, CollectionService>();
builder.Services.AddSingleton<IClock, SystemClock>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
    await dbContext.Database.EnsureCreatedAsync(app.Lifetime.ApplicationStopping);
}

app.MapControllers();
app.Run();

public partial class Program;
