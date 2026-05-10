using FinancialNanoGateway.Application;
using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Infrastructure.Options;
using FinancialNanoGateway.Infrastructure.Services;
using FinancialNanoGateway.Infrastructure.Telemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{typeof(Program).Assembly.GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath);
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource =>
    {
        resource
            .AddService(
                serviceName: "financial-nano-gateway",
                serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0")
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = builder.Environment.EnvironmentName
            });
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter(PaymentMetrics.MeterName)
            .AddView(
                "payment_amount",
                new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = new double[] { 1, 5, 10, 25, 50, 100, 250, 500, 1_000 }
                })
            .AddView(
                "payment_processing_duration_ms",
                new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = new double[] { 25, 50, 100, 250, 500, 1_000, 2_000, 5_000 }
                })
            .AddView(
                "bank_request_duration_ms",
                new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = new double[] { 25, 50, 100, 150, 250, 500, 750, 1_000 }
                })
            .AddOtlpExporter();
    });

builder.Services
    .AddOptions<BankIntegrationOptions>()
    .Bind(builder.Configuration.GetSection(BankIntegrationOptions.SectionName))
    .Validate(
        options => options.MinRequestDelayMs >= 0 &&
                   options.MaxRequestDelayMs >= options.MinRequestDelayMs &&
                   options.FailureRatePercentage is >= 0 and <= 100,
        "Bank integration options must define a valid delay range and failure percentage.")
    .ValidateOnStart();

builder.Services.AddSingleton<PaymentQueue>();
builder.Services.AddSingleton<IPaymentQueue>(provider => provider.GetRequiredService<PaymentQueue>());
builder.Services.AddSingleton<IPaymentMetrics, PaymentMetrics>();
builder.Services.AddSingleton<IBankIntegrationService, BankIntegrationService>();
builder.Services.AddHostedService<PaymentProcessor>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();
