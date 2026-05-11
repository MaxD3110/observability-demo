# Financial Nano-Gateway: Observability Demo

---

## Быстрый старт

Проект полностью готов к работе из коробки. Инфраструктура (Prometheus + Grafana) настроена через **Provisioning**.

1. **Запуск:**
   ```bash
   docker-compose up -d

2. **Доступ к ресурсам:** 
* Swagger (API): http://localhost:8080/swagger
* Prometheus: http://localhost:9090
* Grafana: http://localhost:3000 (Login: admin / Password: admin)
   
---

## Технологический стек

*   **Runtime:** .NET 10 (ASP.NET Core)
*   **Observability:** OpenTelemetry (OTEL) SDK
*   **Storage:** Prometheus (Time-series database)
*   **Visualization:** Grafana (Dashboards & Alerting)
*   **Infrastructure:** Docker Compose

---

## Архитектурные особенности

В проекте реализован принцип **Explicit Metrics** - метрики являются частью контракта приложения, но их реализация скрыта за абстракциями.

*   **Clean Metrics:** Интерфейсы метрик (`IPaymentMetrics`) находятся в слое `Application`, а реализация на базе `System.Diagnostics.Metrics` - в `Infrastructure.Observability`.
*   **High Performance:** Использование структуры `TagList` для записи тегов. Это гарантирует **Zero Allocation** в горячих путях (теги хранятся на стеке), что критично для финтех-систем.
*   **Modern DI:** Использование `IMeterFactory` для корректного управления жизненным циклом метров и упрощения тестирования.

---

## Модель данных мониторинга

Проект демонстрирует три фундаментальных типа метрик:

| Метрика | Тип | Что измеряет |
| :--- | :--- | :--- |
| `payment_processed_total` | **Counter** | Общее кол-во и объем транзакций (фильтры по `currency`, `provider`, `status`). |
| `payment_processing_duration_ms` | **Histogram** | Перцентили (p95, p99) времени обработки. Поиск «выбросов» и лагов. |
| `payment_queue_depth` | **UpDownCounter** | Текущее кол-во платежей в очереди. Индикатор перегрузки (Backpressure). |


Я постарался отразить как можно больше типов и сценариев применения метрик. Описал комментариями в коде важные моменты - на что стоит обратить внимание.

---

## Алертинг

Алерты сконфигурированы как Infrastructure as Code. Grafana автоматически подхватывает правила из папки ./provisioning/alerting. Реализован тестовый алерт на глубину очереди. Получателя алерта необходимо будет настроить вручную, в UI Grafana.

---

## Визуализация (Grafana Dashboards)

Мониторинг в проекте разделен на три логических уровня, что позволяет эффективно использовать данные как бизнесу, так и инженерам:

### 1. Финансовые метрики
Фокусируются на денежных потоках и успешности бизнес-процессов. Позволяют мгновенно оценить "здоровье" продукта с точки зрения прибыли.

![Financial Dashboard](./assets/1.png)

### 2. Операционные метрики
Предназначены для контроля внешних интеграций и качества работы провайдеров.

![Operational Dashboard](./assets/2.png)

### 3. Технические метрики
Подкапотные метрики самого приложения, необходимые для глубокого анализа производительности и отладки.

![Technical Dashboard](./assets/3.png)