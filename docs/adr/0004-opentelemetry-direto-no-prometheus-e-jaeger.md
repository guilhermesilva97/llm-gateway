# ADR 0004: OpenTelemetry com OTLP direto para Prometheus e Jaeger

**Status:** aceita

## Contexto

Quero métricas (tokens, custo, latência, erros, cache, fallbacks) e traces (onde o tempo foi gasto em cada requisição). O desenho clássico usa o OpenTelemetry Collector no meio, mas cada container a mais pesa num projeto que precisa rodar no notebook de quem avalia.

## Decisão

- O gateway instrumenta com o SDK do OpenTelemetry para .NET:
  - um `Meter` próprio (`LlmGateway`), mais as instrumentações de ASP.NET Core e HttpClient;
  - um `ActivitySource` com atributos da convenção semântica de GenAI (`gen_ai.request.model`, `gen_ai.usage.input_tokens` etc.).
- As métricas vão por **OTLP/HTTP direto para o Prometheus 3**, que aceita OTLP nativamente com `--web.enable-otlp-receiver`.
- Os traces vão por OTLP/HTTP direto para o **Jaeger 2**.
- O Grafana é provisionado por arquivo: datasources e dashboard versionados no repositório.

## Consequências

- Três containers de observabilidade em vez de quatro, sem configuração de collector.
- Modelo *push*: o gateway envia as métricas a cada 5 segundos. Funciona mesmo se o gateway estiver atrás de NAT, sem precisar que o Prometheus o alcance.
- O Prometheus converte os nomes: `llm_gateway.requests` vira `llm_gateway_requests_total`, e o histograma em segundos vira `llm_gateway_request_duration_seconds_bucket`. As queries do dashboard usam os nomes convertidos.
- Para amostragem de traces, filtros ou múltiplos destinos, o Collector volta a fazer sentido. Basta trocar as URLs dos exporters.
