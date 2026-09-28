using Ludaryx.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("LudaryxDatabase"));

var app = builder.Build();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();

public partial class Program;
