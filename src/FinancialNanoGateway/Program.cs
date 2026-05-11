using FinancialNanoGateway;
using FinancialNanoGateway.Application;
using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Infrastructure.Services;
using FinancialNanoGateway.Infrastructure.Telemetry;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHealthChecks();
builder.Services.AddTelemetry();
builder.Services.AddApplicationOptions(builder.Configuration);
builder.Services.AddSwagger();

builder.Services.AddSingleton<IPaymentQueue, PaymentQueue>();
builder.Services.AddSingleton<IPaymentMetrics, PaymentMetrics>();
builder.Services.AddSingleton<IBankIntegrationService, BankIntegrationService>();
builder.Services.AddHostedService<PaymentProcessor>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapControllers();

app.Run();