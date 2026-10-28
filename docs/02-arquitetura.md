# 02 - Arquitetura

## Contexto (C4 nível 1)

```mermaid
flowchart TB
    apps([Sistemas clientes<br/>InsightQL, DocFlow, ZapAgent, FarmaRadar])
    ops([Time de plataforma / FinOps])
    gw[LLM Gateway]
    openai[(OpenAI)]
    ollama[(Ollama local)]

    apps -- API compatível com OpenAI --> gw
    ops -- painel admin e Grafana --> gw
    gw -- chat completions --> openai
    gw -- chat completions --> ollama
```

## Containers (C4 nível 2)

```mermaid
flowchart LR
    subgraph Clientes
        app[Projeto cliente<br/>SDK OpenAI]
        browser[Navegador]
    end
    subgraph Docker Compose
        admin[admin<br/>Angular + nginx :8380]
        gw[gateway<br/>.NET 10 :8300]
        pg[(PostgreSQL<br/>clientes e log de uso)]
        redis[(Redis<br/>cache e cotas)]
        prom[(Prometheus 3<br/>OTLP receiver)]
        jaeger[(Jaeger 2)]
        grafana[Grafana :8330]
        ollama[Ollama<br/>profile local-llm]
    end
    openai[(OpenAI API)]

    app -- Bearer gw_... --> gw
    browser --> admin
    admin -- /admin-api + chave admin --> gw
    admin -- /v1 playground --> gw
    gw --> pg
    gw --> redis
    gw -- OTLP métricas --> prom
    gw -- OTLP traces --> jaeger
    grafana --> prom
    grafana --> jaeger
    gw -. fallback .-> ollama
    gw -. se houver chave .-> openai
```

| Container | Responsabilidade |
|---|---|
| **gateway** | Autenticação, rate limit, cota, PII, cache, roteamento com fallback, custo, telemetria e API admin |
| **admin** | Painel Angular servido por nginx; injeta a chave admin no proxy de `/admin-api` |
| **postgres** | Clientes (hash da chave, limites) e log de requisições para o painel |
| **redis** | Cache de respostas e contador de cota mensal por cliente |
| **prometheus / jaeger / grafana** | Métricas, traces e dashboards provisionados por arquivo |

## Pipeline de uma requisição

```mermaid
sequenceDiagram
    autonumber
    participant C as Cliente
    participant A as Auth middleware
    participant R as RateLimiter
    participant G as ChatGateway
    participant Q as Redis (cota/cache)
    participant E as ResilientProviderExecutor
    participant P as Provedor
    participant U as UsageWriter (canal)

    C->>A: POST /v1/chat/completions (Bearer gw_...)
    A->>A: SHA-256 da chave, busca cliente (cache 15s)
    A->>R: cliente identificado
    R->>G: dentro do limite por minuto
    G->>G: resolve alias → cadeia de destinos
    G->>Q: cota do mês disponível?
    G->>G: mascara PII → [CPF_1], [EMAIL_1]
    G->>Q: cache hit? (chave = hash do prompt mascarado)
    alt cache miss
        loop destinos do alias
            G->>E: destino disponível? (circuito)
            E->>P: retry + circuit breaker (Polly)
            P-->>E: resposta ou falha transitória
        end
        G->>Q: grava cache e soma tokens na cota
    end
    G->>G: restaura PII na resposta
    G->>U: registra uso (não bloqueia)
    G-->>C: resposta + cabeçalhos x-gateway-*
```

Pontos importantes:

- **Passo 2:** o banco guarda só o hash da chave ([ADR 0005](adr/0005-chaves-por-cliente-com-hash.md)).
- **Passo 7:** o mascaramento acontece **antes** do cache. Por isso a chave de cache nunca contém dado pessoal, e perguntas iguais de pessoas diferentes compartilham a mesma entrada ([ADR 0002](adr/0002-mascaramento-de-pii-reversivel.md)).
- **Passos 9 a 11:** retry e circuit breaker são por provedor; o fallback é por alias ([ADR 0003](adr/0003-resiliencia-retry-circuit-breaker-fallback.md)).
- **Passo 14:** a gravação no PostgreSQL acontece fora do caminho da requisição, por um `Channel` com escrita em lote. Se o banco ficar lento, o cliente não sente.

## Streaming

O mesmo pipeline vale para `stream: true`, com três cuidados:

1. O gateway sempre pede `stream_options.include_usage` ao provedor, para contabilizar tokens. Se o cliente não pediu, o bloco final de uso é removido antes de chegar a ele.
2. A restauração de PII usa um buffer que segura só o trecho que pode ser um placeholder incompleto. O resto é repassado na hora.
3. O fallback entre destinos só acontece antes do primeiro byte. Uma falha no meio do stream vira um evento de erro no formato da OpenAI.

## Camadas (Clean Architecture)

```mermaid
flowchart TB
    api[LlmGateway.Api<br/>Minimal APIs, middleware, rate limit, OpenTelemetry] --> infra
    api --> core
    infra[LlmGateway.Infrastructure<br/>provedores HTTP, Polly, EF Core, Redis, admin] --> core
    core[LlmGateway.Core<br/>ChatGateway, PII, roteamento, preços, telemetria]
```

| Projeto | Conteúdo | Depende de |
|---|---|---|
| **Core** | Caso de uso `ChatGateway`, `PiiMasker`, `StreamRehydration`, `ModelCatalog`, `PriceTable`, portas (`IProviderExecutor`, `IResponseCache`, `IQuotaStore`, `IUsageSink`) | Só a BCL do .NET |
| **Infrastructure** | `MockChatProvider`, `OpenAiCompatibleProvider`, `ResilientProviderExecutor` (Polly), `RedisResponseCache`, `RedisQuotaStore`, `GatewayDbContext`, `UsageWriter`, `AdminService` | Core, EF Core/Npgsql, StackExchange.Redis, Polly |
| **Api** | `Program`, endpoints `/v1` e `/admin`, autenticação, rate limiter, configuração de OpenTelemetry | Core, Infrastructure |

O Core não conhece HTTP, banco nem Polly. É por isso que 70% dos testes rodam sem rede, com provedores e stores falsos.

## Modelo de dados

```mermaid
erDiagram
    clients ||--o{ request_logs : gera
    clients {
        string Id PK
        string Name
        string KeyHash "SHA-256, único"
        string KeyPreview
        int RequestsPerMinute
        long MonthlyTokenQuota
        bool MaskPii
        bool Active
    }
    request_logs {
        long Id PK
        string ClientId
        timestamptz Timestamp
        string Alias
        string Provider
        string Model
        int PromptTokens
        int CompletionTokens
        decimal CostUsd
        double LatencyMs
        string Status
        bool CacheHit
        int Fallbacks
        int PiiMasked
        string PromptPreview "sempre mascarado"
    }
```

## Métricas expostas

| Métrica (como aparece no Prometheus) | Labels | Uso |
|---|---|---|
| `llm_gateway_requests_total` | client, model, provider, status, cache, stream | Volume, erros, cache hit |
| `llm_gateway_tokens_total` | client, provider, model, type | Consumo de entrada e saída |
| `llm_gateway_cost_usd_total` | client, provider, model | Custo por cliente |
| `llm_gateway_request_duration_seconds` | client, model, provider, status, cache, stream | Latência p50/p95 |
| `llm_gateway_fallbacks_total` | from_provider, reason | Instabilidade de provedores |
| `llm_gateway_pii_masked_total` | type | Volume de dados pessoais protegidos |
| `llm_gateway_circuit_state` | provider | 0 fechado, 1 meio aberto, 2 aberto |

## Decisões

| ADR | Decisão |
|---|---|
| [0001](adr/0001-pipeline-proprio-em-vez-de-yarp.md) | Pipeline próprio em vez de YARP para o tráfego de chat |
| [0002](adr/0002-mascaramento-de-pii-reversivel.md) | Mascaramento de PII reversível por requisição |
| [0003](adr/0003-resiliencia-retry-circuit-breaker-fallback.md) | Retry e circuit breaker por provedor, fallback por modelo |
| [0004](adr/0004-opentelemetry-direto-no-prometheus-e-jaeger.md) | OTLP direto para Prometheus e Jaeger, sem Collector |
| [0005](adr/0005-chaves-por-cliente-com-hash.md) | Uma chave por cliente, armazenada só como hash |
