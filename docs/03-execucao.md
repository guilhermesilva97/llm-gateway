# 03 - Execução

Como o projeto foi construído, na ordem em que aconteceu, com as decisões e os problemas reais encontrados.

## Etapas

| # | Etapa | Entregável |
|---|---|---|
| 1 | Contrato e domínio | Núcleo (`LlmGateway.Core`) sem dependências externas |
| 2 | Privacidade | Mascaramento e restauração de PII, inclusive em streaming |
| 3 | Provedores e resiliência | Provedor compatível com OpenAI, provedor mock, Polly por provedor |
| 4 | Orquestração | `ChatGateway`: cota, PII, cache, fallback, custo, telemetria |
| 5 | API | Minimal APIs, autenticação por chave, rate limit por cliente, admin |
| 6 | Persistência | EF Core no PostgreSQL, gravação de uso assíncrona |
| 7 | Observabilidade | OpenTelemetry → Prometheus/Jaeger → Grafana provisionado |
| 8 | Painel | Angular 22 com signals, zoneless, servido por nginx |
| 9 | Carga e smoke | Scripts k6 para popular o dashboard e validar a plataforma |
| 10 | Pipeline | GitHub Actions com testes, smoke ponta a ponta e publicação |
| 11 | Teste com a OpenAI real | Roteamento, PII, streaming, cache e custo contra o provedor real |

---

## Etapa 1: contrato e domínio

Comecei pelo que **não** muda: o contrato da OpenAI e as regras de negócio. O projeto `Core` não referencia nenhum pacote além da BCL do .NET.

**Decisão: tratar o corpo como `JsonObject`, e não como DTO.** A API da OpenAI tem dezenas de campos (`tools`, `response_format`, `logprobs`, `seed`…) e ganha campos novos a cada mês. Se eu modelasse classes, qualquer campo que eu não conhecesse seria descartado silenciosamente. Com `JsonObject`, o gateway mexe só no que precisa (`model`, `messages[].content`, `stream`) e repassa o resto intacto. Isso permite que o InsightQL, que usa *tool calling*, passe pelo gateway sem nenhuma mudança.

## Etapa 2: privacidade

- **Regex para detectar, validação para confirmar.** CPF e CNPJ sem pontuação só são mascarados se o dígito verificador bater. Sem isso, qualquer código de rastreio com 11 dígitos vira "CPF".
- **CNPJ alfanumérico.** Desde julho de 2026 a Receita emite CNPJs com letras (ex.: `12.ABC.345/01DE-35`). O cálculo do dígito usa o valor ASCII − 48 de cada caractere. Escrevi o validador pelos dois exemplos da especificação e deixei os dois formatos cobertos por testes.
- **Placeholders numerados por tipo**, com o mesmo valor sempre gerando o mesmo placeholder dentro da requisição. Isso deixa a conversa coerente para o modelo.

**O caso difícil foi o streaming.** O provedor pode quebrar `[EMAIL_1]` em pedaços como `[EM` + `AIL_1]`. O `StreamingRehydrator` segura apenas o trecho a partir do último `[` sem `]`, e só enquanto ele for curto o bastante para ainda ser um placeholder (24 caracteres). Assim, um texto comum com colchetes não fica preso. No fim do stream (`finish_reason`), o que sobrou é liberado. Os testes cobrem placeholders quebrados em vários chunks, colchetes que não são placeholders e texto pendente no final.

## Etapa 3: provedores e resiliência

- `OpenAiCompatibleProvider` atende OpenAI, Ollama e qualquer serviço com o mesmo contrato. Ele classifica erros:
  - conexão, timeout, 429 e 5xx são **transitórios**;
  - 400, 401 e 404 são **rejeição**.
- `MockChatProvider` é determinístico: responde ecoando o que recebeu. Isso permite **ver** o mascaramento, porque o mock devolve `[CPF_1]` exatamente como recebeu. Ele aceita latência e taxa de falha configuráveis, o que alimenta o dashboard com fallbacks reais.
- Cada provedor tem seu pipeline Polly (retry exponencial com jitter → circuit breaker). Rejeições não contam para o circuito, porque um prompt inválido não significa que o provedor está fora.

## Etapa 4: orquestração

O `ChatGateway` é o caso de uso central. A ordem das etapas importa:

1. Valida o alias e a cota **antes** de qualquer custo.
2. Mascara a PII **antes** do cache. Assim, a chave de cache nunca contém dado pessoal, e duas pessoas fazendo a mesma pergunta com e-mails diferentes compartilham a entrada. Cada uma recebe a resposta restaurada com o próprio e-mail. Percebi esse efeito ao definir a ordem das etapas e o transformei num caso de teste explícito.
3. Percorre a cadeia de destinos do alias, pulando os indisponíveis.
4. Calcula o custo pela tabela de preços. Se o provedor não informar `usage`, estima por caracteres ÷ 4 e registra a estimativa.
5. Restaura a PII e registra o uso.

Toda a telemetria de uma requisição (métricas, span, registro no banco) fica num `RequestTracker`. Ele garante que cada requisição seja contabilizada **uma única vez**, com sucesso ou com falha.

## Etapa 5: API

- Autenticação num middleware que roda **antes** do rate limiter. Assim o limiter já sabe qual cliente particionar.
- `RateLimiter` do ASP.NET Core com uma janela fixa por cliente. A chave da partição inclui o limite (`cliente:rpm`), para que uma mudança de limite pelo painel crie um limiter novo.
- O 429 de rate limit também é registrado como requisição (status `rate_limited`). Sem isso, o dashboard não mostraria quem está sendo bloqueado.
- A imagem `aspnet` não tem `curl`. Para o *healthcheck* do Docker, o próprio binário aceita `--healthcheck` e faz um GET em `/health`.

## Etapa 6: persistência

> **Problema:** o caminho natural seria usar *migrations* do EF Core. Só que os arquivos gerados pelo `dotnet ef migrations add` vêm com comentários XML (`/// <inheritdoc />`), e o padrão do projeto é não ter comentários no código.
>
> **Decisão:** usar `EnsureCreated` com um *retry* de inicialização enquanto o PostgreSQL sobe. É uma troca consciente, registrada como limitação: para evoluir o schema em produção, migrations voltam a ser necessárias.

A gravação do log de uso **não** acontece na requisição: o `RequestTracker` publica num `Channel` limitado (10 mil itens, descarta os mais antigos se lotar) e um `BackgroundService` grava em lotes de até 200. Se o banco ficar lento ou cair, o gateway continua respondendo e só perde registros de log, nunca requisições.

> **Problema:** configuração do .NET usa `:` como separador de seção. O modelo do Ollama se chama `qwen2.5:7b`, então não podia ser **chave** de dicionário no `appsettings.json`: o `:` seria interpretado como uma subseção.
>
> **Decisão:** preços e rotas viraram **listas** de objetos (`{ "Target": "ollama/qwen2.5:7b", ... }`), em que o nome do modelo é um valor, não uma chave.

## Etapa 7: testes de integração

O `WebApplicationFactory` sobe a API inteira em memória. Troquei Redis por implementações em memória e o PostgreSQL pelo provedor InMemory do EF Core.

> **Problema:** arrays na configuração do .NET são mesclados **por índice**. Ao sobrescrever a lista de modelos nos testes, os itens do `appsettings.json` que estavam em índices maiores continuariam lá, misturados.
>
> **Decisão:** nos testes, uso `PostConfigure<GatewayOptions>` para substituir as listas inteiras depois do *binding*, em vez de empilhar mais uma fonte de configuração.

## Etapa 8: painel Angular

- Gerei o esqueleto com a CLI do Angular 22 dentro de um container e removi os comentários que a CLI coloca nos `tsconfig*.json` e no `.editorconfig`.
- Componentes standalone, `signal`/`computed`, controle de fluxo nativo (`@if`, `@for`) e aplicação zoneless, o padrão da versão 22.
- O playground usa `fetch` com `ReadableStream` e um parser de SSE próprio, porque `EventSource` só faz GET.
- O nginx do painel injeta a chave admin no proxy de `/admin-api`. A chave nunca vai para o navegador.
- Os testes rodam com o builder `@angular/build:unit-test` (Vitest + jsdom), sem precisar de Chrome no container.

## Etapa 9: observabilidade, carga e smoke

- Métricas com nomes no padrão OpenTelemetry (`llm_gateway.requests`, unidade `{request}`). O Prometheus 3 converte esses nomes ao receber por OTLP.
- Antes de escrever o dashboard, conferi no próprio Prometheus os nomes convertidos: `llm_gateway_requests_total`, `llm_gateway_request_duration_seconds_bucket`, `llm_gateway_circuit_state`. Depois rodei **cada query de cada painel** contra o Prometheus para garantir que nenhum painel abre vazio.
- O histograma de latência usa limites de bucket explícitos (`InstrumentAdvice`). Os limites padrão do SDK são pensados para milissegundos e ficariam inúteis numa métrica em segundos.

> **Problema:** na primeira execução do smoke test, tudo passou menos o Grafana, com *connection refused*. O container estava "Up", mas o Grafana 13 leva alguns segundos migrando o banco interno antes de abrir a porta 3000. O log mostrou `HTTP Server Listen` dois segundos **depois** do check.
>
> **Correção:** healthcheck real no compose (`/api/health`), o smoke passou a depender de `grafana: service_healthy`, e o script espera o Grafana responder antes de consultar o dashboard.
>
> **Lição:** "container rodando" não é "serviço pronto". Todo serviço de que outro depende ganhou healthcheck.

> **Problema:** a primeira rodada de carga teve 5,5% de respostas diferentes de 200 no cenário principal. Investigando pelo Prometheus (`sum by (status)`), eram 429: o próprio script mandava cerca de 15 req/s divididas entre 4 clientes, acima do limite de 120 por minuto de cada um.
>
> **Correção:** reduzi o pico para 6 req/s. Assim, o cenário principal mede os provedores e só o cenário `sandbox` exercita o rate limit, de propósito.

> **Problema:** durante um teste, interrompi o k6 no meio (o `head` do terminal fechou o pipe). As requisições em andamento apareceram como `cancelled` (HTTP 499) no Prometheus.
>
> Não precisei corrigir nada: isso confirmou que o `RequestTracker` contabiliza conexões abortadas pelo cliente, inclusive em streaming.

> **Problema:** no Git Bash do Windows, `docker compose run k6 run /scripts/load.js` falhou porque o MSYS converteu `/scripts/load.js` para `C:/Program Files/Git/scripts/load.js`.
>
> **Correção:** documentei o uso de `MSYS_NO_PATHCONV=1`. O comando padrão do compose, sem argumentos, não é afetado.

## Etapa 10: pipeline

Detalhada em [04-pipeline.md](04-pipeline.md). Ela roda os mesmos comandos `docker compose` do ambiente local, e o smoke test em k6 é o mesmo que usei para validar a plataforma.

## Etapa 11: teste com a OpenAI real

Com a chave configurada em `OPENAI_API_KEY`, repeti os cenários principais contra o provedor real.

- **Roteamento:** o alias `default` foi direto para `openai/gpt-4o-mini`, com `X-Gateway-Fallbacks: 0`. Sem chave, o mesmo alias caía para o mock com 2 fallbacks; a cadeia de fallback só é percorrida quando precisa.
- **PII:** enviei um CPF e um e-mail pedindo para o modelo repetir o texto. O cliente recebeu os valores originais, e o log gravou `[CPF_1]` e `[EMAIL_1]` com `piiMasked: 2`. A OpenAI recebeu só os marcadores.
- **Streaming:** 9 *chunks* terminando em `[DONE]`, com o CPF restaurado no texto final mesmo chegando fragmentado entre *chunks*.
- **Cache:** a mesma pergunta duas vezes deu `miss` e depois `hit`, e a segunda não chegou à OpenAI.
- **Custo:** `X-Gateway-Cost-Usd` calculado a partir do `usage` real (ex.: 48 tokens → US$ 0,00001485 no `gpt-4o-mini`).
- **Métricas:** o Prometheus mostrou as requisições separadas por provedor (`openai` e `cache`).

> **Observação:** a primeira chamada logo depois de a stack ficar saudável não retornou resposta HTTP. A mesma chamada, repetida em seguida, funcionou, e não consegui reproduzir a falha. Registro aqui porque é exatamente o tipo de coisa que eu investigaria com os traces do Jaeger se voltasse a acontecer.

> **Decisão:** o gateway não precisou da correção de `temperature`/`reasoning_effort` que fiz nos outros projetos. Esses parâmetros vêm no corpo da requisição do cliente e são repassados ao provedor; quem escolhe é o cliente, não o gateway.

---

## Validações realizadas

| Verificação | Resultado |
|---|---|
| `dotnet format --verify-no-changes` | Sem alterações |
| Testes do gateway (unitários + integração com `WebApplicationFactory`) | 100 aprovados |
| Build de produção e testes do painel (Vitest) | 9 aprovados |
| Smoke test ponta a ponta (k6, stack completa) | 22 de 22 checks |
| Provedor recebe `[CPF_1]`/`[EMAIL_1]`; cliente recebe os valores originais | OK, com e sem streaming |
| Alias `default` sem chave da OpenAI e sem Ollama | Respondeu pelo `mock` com 2 fallbacks |
| Cliente `sandbox` (5 req/min) | 429 a partir da 6ª requisição, com `Retry-After` |
| Carga de 1 minuto com provedor instável (35% de falhas) | Circuit breaker abriu para `ollama` e `mock-unstable`, visível em `llm_gateway_circuit_state` |
| Métricas no Prometheus via OTLP | Todas as 7 métricas presentes |
| Queries dos 15 painéis do Grafana | Todas retornando séries |
| Traces no Jaeger | Serviço `llm-gateway` registrado |
| Painel Angular via nginx (overview, clientes, requisições, criação de cliente) | OK contra PostgreSQL real |
| Workflow validado com actionlint | Sem erros |
| OpenAI real: roteamento, PII, streaming, cache e custo | OK (detalhes na etapa 11) |

**Não validado:** chamadas reais ao Ollama. O comportamento de falha dele (conexão recusada, DNS inexistente) foi exercitado de verdade com o serviço desligado.

## Próximos passos

- Cache semântico: embeddings do prompt mascarado e busca por similaridade, para respostas a perguntas parecidas, e não só idênticas.
- Rate limit e estado de circuito distribuídos no Redis, para rodar várias réplicas.
- Migrations do EF Core geradas num passo de build, sem versionar os arquivos gerados.
- Login no painel com OIDC no lugar da chave injetada pelo nginx.
- Detector de PII baseado em NER para nomes e endereços.
- Endpoint `/v1/embeddings` em passthrough (aqui o YARP faria sentido).
