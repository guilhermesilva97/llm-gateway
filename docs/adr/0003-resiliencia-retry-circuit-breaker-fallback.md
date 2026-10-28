# ADR 0003: retry e circuit breaker por provedor, fallback por modelo

**Status:** aceita

## Contexto

Provedores de LLM falham com frequência: limite de taxa (429), instabilidade (5xx), timeout em respostas longas. Um modelo local pode simplesmente não estar ligado. Os projetos clientes não deveriam ter que tratar isso cada um do seu jeito.

## Decisão

Separei o tratamento em dois níveis:

1. **Por provedor (infraestrutura):** cada provedor tem seu próprio pipeline Polly, com retry exponencial com jitter e circuit breaker.
   - Erros transitórios (conexão, timeout, 429 e 5xx) são repetidos e contam para o circuito.
   - Erros do cliente (400, 401, 404) **não** são repetidos nem abrem o circuito, porque a culpa é da requisição, não do provedor.
2. **Por modelo (aplicação):** cada alias tem uma **cadeia de destinos**, por exemplo `default → openai/gpt-4o-mini → ollama/qwen2.5:7b → mock/mock-1`.
   - O `ChatGateway` percorre a cadeia: pula destinos indisponíveis (circuito aberto ou sem credencial) e passa para o próximo quando um falha depois dos retries.
   - Em streaming, o fallback só acontece antes do primeiro byte. Depois disso, a resposta já começou a ir para o cliente.

## Consequências

- **O circuito aberto evita o "efeito manada":** depois de algumas falhas, o provedor é isolado por um tempo e as requisições vão direto para o próximo destino, sem esperar timeout.
- O estado do circuito vira métrica (`llm_gateway_circuit_state`) e aparece no Grafana e no painel.
- O cliente recebe nos cabeçalhos qual provedor respondeu (`x-gateway-provider`) e quantos destinos foram pulados (`x-gateway-fallbacks`).
- **Trocar de modelo muda a resposta.** Fallback entre modelos de qualidade diferente é uma decisão de negócio, e por isso fica explícita na configuração de cada alias.
- O estado do circuito é por instância. Com várias réplicas, cada uma aprende sozinha. Aceitável para este projeto; um estado compartilhado em Redis seria o próximo passo.
