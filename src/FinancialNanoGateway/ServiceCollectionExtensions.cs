using FinancialNanoGateway.Infrastructure.Observability;
using FinancialNanoGateway.Infrastructure.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

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
                    // Exemplars: each histogram data point gets the TraceId of the request that produced the sample.
                    // TraceBased records an exemplar only if the sample was taken inside a sampled span.
                    .SetExemplarFilter(ExemplarFilterType.TraceBased)
                    // A View lets you override a metric's settings:
                    .AddView(
                        "payment_amount",
                        new ExplicitBucketHistogramConfiguration
                        {
                            Name = "payment_amount_custom_name", // Change the metric name
                            Description = "Custom description", // Change the metric description
                            CardinalityLimit = 10, // Tune the metric's cardinality
                            TagKeys = ["restrictedTag"], // Drop unneeded tags (labels) to save storage space
                            Boundaries = [1, 5, 10, 25, 50, 100, 250, 500, 1_000] // Tune histogram buckets for accurate P95/P99
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

            openTelemetry.WithTracing(tracing =>
            {
                tracing
                    // Auto-instrumentation: creates a SERVER span for every incoming HTTP request.
                    // This is the root of the request-side trace, to which our PRODUCER span attaches.
                    .AddAspNetCoreInstrumentation()
                    // Register our domain ActivitySource. Without AddSource its spans would be ignored.
                    .AddSource(PaymentTracing.ActivitySourceName)
                    // Sampling decides which traces to record.
                    // ParentBased guarantees consistency: if the parent is sampled, the child is too.
                    // In practice: use head sampling (a fraction, e.g. 10%) or tail sampling
                    // on the Collector (record only slow/error traces) to control storage volume.
                    .SetSampler(new ParentBasedSampler(new AlwaysOnSampler()))
                    .AddOtlpExporter();
            });

            // Logs go through the same unified builder, so they share the Resource (service.name, etc.) with metrics
            // and traces. The provider is a bridge: every ILogger record becomes an OTLP log record. The key point -
            // if a record is emitted inside an active Activity, it automatically carries TraceId/SpanId.
            // That is exactly what links logs to traces (span <-> logs pivot in Grafana) without any manual code.
            openTelemetry.WithLogging(logging => logging.AddOtlpExporter());

            services.Configure<OpenTelemetryLoggerOptions>(options =>
            {
                options.IncludeScopes = true;
                options.IncludeFormattedMessage = true;
                options.ParseStateValues = true;
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
            services.Configure<BankAIntegrationOptions>(
                configuration.GetSection(nameof(BankAIntegrationOptions)));
            
            services.Configure<BankBIntegrationOptions>(
                configuration.GetSection(nameof(BankBIntegrationOptions)));
        }
    }
}