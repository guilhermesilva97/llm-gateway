# ADR 0005: uma chave por cliente, armazenada só como hash

**Status:** aceita

## Contexto

Cada projeto do portfólio (InsightQL, DocFlow, ZapAgent, FarmaRadar) consome o gateway. Preciso saber quem gastou o quê, limitar quem abusa e conseguir revogar acesso sem afetar os outros.

## Decisão

- Cada cliente tem uma chave própria (`gw_` + 32 bytes aleatórios em base64url).
- O banco guarda apenas o **SHA-256 da chave** e um prefixo para exibição. A chave completa aparece uma única vez, na criação ou na rotação.
- A busca do cliente pela chave usa cache em memória de 15 segundos para não consultar o banco a cada requisição.
- Cada cliente tem:
  - **rate limit por minuto**, com o `RateLimiter` do ASP.NET Core particionado por cliente;
  - **cota mensal de tokens**, com contador no Redis por cliente e mês.
- A API administrativa (`/admin`) é protegida por uma chave separada, comparada em tempo constante.

## Consequências

- Um vazamento do banco não expõe chaves utilizáveis.
- Desativar um cliente ou rotacionar a chave leva até 15 segundos para valer em todas as requisições, por causa do cache. É uma troca consciente entre latência e consistência.
- O rate limiter é em memória, por instância. Com várias réplicas, o limite efetivo multiplica. O próximo passo seria um limiter distribuído no Redis.
- O painel administrativo chega na API admin por um nginx que injeta a chave admin. Isso é adequado para uma demo local; em produção, o painel precisaria de login (OIDC) e a chave nunca ficaria no proxy.
