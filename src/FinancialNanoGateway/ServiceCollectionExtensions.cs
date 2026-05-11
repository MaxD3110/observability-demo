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
            const string serviceName = "financial-nano-gateway";
            var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";
            
            var openTelemetry = services.AddOpenTelemetry()
                .ConfigureResource(resource =>
                {
                    resource
                        .AddService(
                            serviceName: serviceName,
                            serviceVersion: serviceVersion);
                });

            openTelemetry.WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(PaymentMetrics.MeterName)
                    // View позволяет переопределить настройки метрики:
                    .AddView(
                        "payment_amount",
                        new ExplicitBucketHistogramConfiguration
                        {
                            Name = "payment_amount_custom_name", // Изменить имя метрики
                            Description = "Custom description", // Изменить описание метрики
                            CardinalityLimit = 10, // Настроить кардинальность метрики
                            TagKeys = ["restrictedTag"], // Отфильтровать ненужные теги (Labels) для экономии места в хранилище
                            Boundaries = [1, 5, 10, 25, 50, 100, 250, 500, 1_000] // Настроить корзины (Buckets) гистограмм для точного расчета P95/P99
                        })
                    .AddView(
                        "payment_queue_wait_duration_milliseconds",
                        new ExplicitBucketHistogramConfiguration
                        {
                            Boundaries = [1, 5, 10, 25, 50, 100, 250, 500, 1_000, 2_000]
                        })
                    .AddView(
                        "payment_processing_duration_milliseconds",
                        new ExplicitBucketHistogramConfiguration
                        {
                            Boundaries = [25, 50, 100, 250, 500, 1_000, 2_000, 5_000]
                        })
                    .AddView(
                        "bank_request_duration_milliseconds",
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
            services.AddOptions<BankAIntegrationOptions>()
                .Bind(configuration.GetSection(nameof(BankAIntegrationOptions)));

            services.AddOptions<BankBIntegrationOptions>()
                .Bind(configuration.GetSection(nameof(BankBIntegrationOptions)));
        }
    }
}