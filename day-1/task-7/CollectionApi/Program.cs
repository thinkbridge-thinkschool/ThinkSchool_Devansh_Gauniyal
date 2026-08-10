using CollectionApi.Data;
using CollectionApi.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<CollectionDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Collections")));
builder.Services.AddScoped<ICollectionRepository, CollectionRepository>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

app.MapControllers();
app.Run();

public partial class Program;
