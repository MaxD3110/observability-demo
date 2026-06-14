using System.Threading.Channels;
using FinancialNanoGateway;
using FinancialNanoGateway.Application;
using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Application.Dtos;
using FinancialNanoGateway.Infrastructure.Observability;
using FinancialNanoGateway.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHealthChecks();
builder.Services.AddObservability();
builder.Services.AddApplicationOptions(builder.Configuration);
builder.Services.AddSwagger();

builder.Services.AddSingleton<IBankIntegrationService, BankAIntegrationService>();
builder.Services.AddSingleton<IBankIntegrationService, BankBIntegrationService>();

// The queue's backing channel is a shared singleton so PaymentMetrics can read its depth
// (Reader.Count) on demand for the observable gauge - without creating a queue<->metrics cycle
// (the channel depends on nothing, both services depend on it).
builder.Services.AddSingleton(Channel.CreateUnbounded<PaymentMessageEnvelopeDto>());
builder.Services.AddSingleton(sp => sp.GetRequiredService<Channel<PaymentMessageEnvelopeDto>>().Reader);

builder.Services.AddSingleton<IPaymentQueue, PaymentQueue>();
builder.Services.AddSingleton<IPaymentMetrics, PaymentMetrics>();
builder.Services.AddSingleton<IPaymentTracing, PaymentTracing>();
builder.Services.AddHostedService<PaymentProcessor>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapControllers();

app.Run();