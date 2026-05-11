using FinancialNanoGateway.Infrastructure.Observability;
using FinancialNanoGateway.Infrastructure.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace FinancialNanoGateway;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public void AddObservability()
        {
            var openTelemetry = services.AddOpenTelemetry()
                .ConfigureResource(resource =>
                {
                    resource
                        .AddService(
                            serviceName: "financial-nano-gateway",
                            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0");
                });

            openTelemetry.WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(PaymentMetrics.MeterName)
                    .AddView(
                        "payment_amount",
                        new ExplicitBucketHistogramConfiguration
                        {
                            Boundaries = [1, 5, 10, 25, 50, 100, 250, 500, 1_000]
                        })
                    .AddView(
                        "payment_processing_duration_ms",
                        new ExplicitBucketHistogramConfiguration
                        {
                            Boundaries = [25, 50, 100, 250, 500, 1_000, 2_000, 5_000]
                        })
                    .AddView(
                        "bank_request_duration_ms",
                        new ExplicitBucketHistogramConfiguration
                        {
                            Boundaries = [25, 50, 100, 150, 250, 500, 750, 1_000]
                        })
                    .AddOtlpExporter();
            });
        }

        public void AddSwagger()
        { 
            services.AddSwaggerGen(options =>
            {
                var xmlFile = $"{typeof(Program).Assembly.GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                options.IncludeXmlComments(xmlPath);
            });
        }

        public void AddApplicationOptions(IConfiguration configuration)
        {
            services
                .AddOptions<BankIntegrationOptions>()
                .Bind(configuration.GetSection(nameof(BankIntegrationOptions)));
        }
    }
}