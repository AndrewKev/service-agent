var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register ApiKeyService as singleton
builder.Services.AddSingleton<service_agent.Services.ApiKeyService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

// Print the generated API key to the console
var apiKeyService = app.Services.GetRequiredService<service_agent.Services.ApiKeyService>();
Console.WriteLine($"[API KEY] {apiKeyService.ApiKey}");

app.Run();
