using service_agent.Options;
using service_agent.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register ApiKeyService as singleton
builder.Services.AddSingleton<ApiKeyService>();
builder.Services.Configure<ServiceMonitoringOptions>(
    builder.Configuration.GetSection(ServiceMonitoringOptions.SectionName));
builder.Services.AddHttpClient("ServiceMonitoringManagementClient");
builder.Services.AddSingleton<IRegisteredServiceClient, RegisteredServiceClient>();
builder.Services.AddSingleton<ISystemdServiceReader, SystemdServiceReader>();
builder.Services.AddSingleton<IAlertClient, AlertClient>();
builder.Services.AddHostedService<ServiceMonitoringWorker>();

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
var apiKeyService = app.Services.GetRequiredService<ApiKeyService>();
Console.WriteLine($"[API KEY] {apiKeyService.ApiKey}");

app.Run();
