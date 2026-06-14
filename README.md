
[![GitHub](https://img.shields.io/badge/GitHub-%23121011.svg?logo=github&logoColor=white)](https://github.com/MaxD3110)
[![LinkedIn](https://custom-icon-badges.demolab.com/badge/LinkedIn-0A66C2?logo=linkedin-white&logoColor=fff)](https://www.linkedin.com/in/maxim-anisavets/)

<div id="readme-top"></div>

# Financial Nano-Gateway: Observability Demo
[![C#](https://custom-icon-badges.demolab.com/badge/C%23-%23239120.svg?logo=cshrp&logoColor=white)](#)
[![OpenTelemetry](https://img.shields.io/badge/OpenTelemetry-000?logo=opentelemetry&logoColor=fff)](#)

## Quick start

The project works out of the box. The infrastructure (Prometheus + Tempo + Loki + Grafana) is wired up via **provisioning**.

1. **Run this in a project directory:**
   ```bash
   docker-compose up -d

2. **Access the resources:**
* Swagger (API): http://localhost:8080/swagger
* Prometheus: http://localhost:9090
* Grafana: http://localhost:3000 (Login: admin / Password: 12345)

---

## Tech stack

*   **Runtime:** .NET 10 (ASP.NET Core)
*   **Observability:** OpenTelemetry (OTEL) SDK
*   **Storage:** Prometheus (metrics), Tempo (traces), Loki (logs)
*   **Visualization:** Grafana (Dashboards & Alerting)
*   **Infrastructure:** Docker Compose

---

## Architecture highlights

The project follows the **Explicit Metrics** principle - metrics are part of the application's contract, but their implementation is hidden behind abstractions.

*   **Clean Metrics:** The metric interfaces (`IPaymentMetrics`) live in the `Application` layer, while the `System.Diagnostics.Metrics`-based implementation lives in `Infrastructure.Observability`.
*   **High Performance:** The `TagList` struct is used to record tags. This guarantees **Zero Allocation** on hot paths (tags are kept on the stack), which is critical for fintech systems.
*   **Modern DI:** `IMeterFactory` is used to manage the lifecycle of meters correctly and to simplify testing.

---

## Monitoring data model

The project defines ~12 instruments (see [`PaymentMetrics.cs`](src/FinancialNanoGateway/Infrastructure/Observability/PaymentMetrics.cs)); the table below is an illustrative slice covering each instrument family:

| Metric | Type | What it measures |
| :--- | :--- | :--- |
| `payment_processed` | **Counter** | Monotonic count of processed transactions (filterable by `currency`, `outcome`). |
| `payment_processing_duration` | **Histogram** | Distribution / percentiles (p95, p99) of processing time. Hunting for outliers and lags. |
| `payment_active_processing` | **UpDownCounter** | Payments being processed right now - a value that goes up and down. |
| `payment_queue_depth` | **Observable Gauge** | Current number of payments waiting in the queue. A backpressure / overload indicator. |

### Synchronous vs observable instruments

The last two rows both track a "current level" but pick **opposite** instrument families on purpose - this is the distinction worth internalizing:

* **Synchronous** (`Counter`, `Histogram`, `UpDownCounter`) - you record the measurement **inline, at the exact line where the event happens** (*push*). `payment_active_processing` does `Add(+1)`/`Add(-1)` at the start/end of processing, because the code is already standing on those events.
* **Observable** (`ObservableGauge`, `ObservableCounter`, `ObservableUpDownCounter`) - you hand the SDK a **callback that reads a current value on demand**, invoked when the collector scrapes (*pull*). `payment_queue_depth` just samples the channel's own `Reader.Count`; there is no per-change event to hook, and reading the value when asked is simpler than instrumenting every enqueue/dequeue.

Rule of thumb: reach for an **observable** instrument when you can *read* a value on demand but don't have (or don't want to instrument) the individual events that change it - runtime stats (CPU, memory, GC), pool/cache sizes, a queue length read from the queue itself.

The important points are explained in code comments - what's worth paying attention to.

---

## Tracing (Distributed Tracing)

End-to-end tracing via **OpenTelemetry -> Tempo**. The key thing being demonstrated is stitching the two halves of a request across the async boundary: the HTTP request only puts the payment on a queue (`Channel`) and immediately returns `202`, while the actual processing happens later on a different thread.

The queue is treated as a message broker: the trace context (W3C `traceparent`) travels in the envelope headers, exactly as it would with Kafka/RabbitMQ. Following the messaging convention, the producer and consumer are separate traces joined by a **span link** (PRODUCER -> CONSUMER -> CLIENT). Unlike metrics, spans deliberately carry high-cardinality fields (`payment.id`, the exact amount) - a span is a single request, not an aggregate, and the more context per request, the faster the debugging. Errors are recorded right on the span - in Tempo that's a red span with a stack trace.

A span also carries two things beyond tags: **events** (`bank.dispatch` - a timestamped checkpoint *inside* a span, marking *when* something happened) and **baggage** - a `payment.priority` value set at the edge that rides across the async boundary so the consumer can read it without the producer re-passing it.

![Tracing Demo](./assets/5.png)

---

## Logs (Structured Logging)

Logs go via `ILogger` -> **OpenTelemetry -> Loki**.

**Structured logging.** Instead of baking values into a finished string, you log a *message template with named fields* (`"Payment processed. PaymentId={PaymentId}, DurationMs={DurationMs}"`). The values travel as data, so in Loki you can filter and aggregate on them (`DurationMs > 1000`) instead of grepping prose. The anti-pattern is string interpolation (`$"...{id}..."`), which flattens everything into one opaque string *before* it's logged, throwing the structure away.

**Log levels are a cost/verbosity dial.** `BankRequestStarting` is a `Debug` line; production runs at `Information`, so it is **never emitted in prod**. That's the point - you instrument richly at `Debug` and only pay the volume/cost/noise when you raise the level to chase a problem. "Invisible in prod" is the feature, not a bug.

**Scopes.** `BeginScope` attaches `PaymentId`/`Currency` to *every* log inside the block (including the bank service's logs) without threading them through each call.

**Formatted message vs fields.** The rendered message string is for a human skimming logs; in production you query on the **fields** and the `EventId`, not the prose (the demo ships both via `IncludeFormattedMessage`).

**`[LoggerMessage]` source generator.** This is an *optimization layered on top of* normal structured logging, not a different way of logging. The generator expands each call at compile time into allocation-free code: no boxing, no `params object[]`, no template parsing at runtime, and the level is checked *before* the arguments are evaluated - the logging counterpart of the zero-allocation `TagList` story from the metrics. Compare the plain `logger.LogWarning(...)` in [`PaymentsController.cs`](src/FinancialNanoGateway/Api/Controllers/PaymentsController.cs) with the source-generated version of the same idea in [`PaymentLog.cs`](src/FinancialNanoGateway/Application/Logging/PaymentLog.cs).

**Cardinality discipline** (same as metrics): only low-cardinality fields (`service_name`) become Loki **labels**, while `trace_id` and `payment.id` go to **structured metadata**.

![Logs Demo](./assets/7.png)

---

## Correlating the three pillars

The headline feature of the demo is jumping metric -> trace -> log in a couple of clicks:

*   **Metric -> trace:** histograms have **exemplars** enabled (points carrying a `trace_id`) - clicking one jumps straight into Tempo.
*   **Trace -> logs:** from a span, the "Logs for this span" button opens Loki filtered by `trace_id`.
*   **Log -> trace:** the `trace_id` field in a log is a clickable link back into Tempo.

So a "spike on the latency graph" unfolds in a couple of clicks into the specific failed payment and its log with the stack trace.

![Correlation Demo1](./assets/8.png)
![Correlation Demo2](./assets/6.png)

---

## Alerting

Alerts are configured as Infrastructure as Code. Grafana automatically picks up the rules from ./provisioning/alerting. There is an alert on the % of failed bank outcomes, delivered to Telegram through a custom, readable notification template. The bot token is supplied via an environment variable (`.env`, git-ignored), not stored in the repo.

![Alerting Demo](./assets/4.png)

---

## Visualization (Grafana Dashboards)

Monitoring in the project is split into three logical levels, so the data is useful both to the business and to engineers:

### 1. Financial metrics
Focus on money flows and the success of business processes. They let you instantly gauge the product's "health" from a revenue perspective.

![Financial Dashboard](./assets/1.png)

### 2. Operational metrics
Intended for monitoring external integrations and the quality of provider performance.

![Operational Dashboard](./assets/2.png)
![Operational Dashboard](./assets/9.png)

### 3. Technical metrics
Under-the-hood metrics of the application itself, needed for deep performance analysis and debugging.

![Technical Dashboard](./assets/3.png)

<p align="right">
   <a href="#readme-top">Back to top ^</a>
</p>
