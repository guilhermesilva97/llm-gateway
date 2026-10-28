# 01 - Ideia

## O problema

Quando uma empresa começa a usar LLM, cada time integra do seu jeito: um usa a OpenAI direto, outro sobe um modelo local, outro usa a Azure. Em poucos meses aparecem as mesmas perguntas, e ninguém sabe responder:

- **Quanto estamos gastando, e com o quê?** A fatura chega consolidada, sem dizer qual sistema consumiu.
- **Estamos mandando dados de clientes para fora?** Um CPF colado num prompt de atendimento já é um problema de LGPD.
- **O que acontece quando o provedor cai?** Cada sistema descobre na hora, em produção.
- **Quem pode usar qual modelo, e quanto?** Uma chave compartilhada não diz quem abusou.

Eu vivi isso integrando LLMs em APIs e automações. O LLM Gateway é a peça que eu gostaria de ter tido desde o começo: **um único ponto de entrada** para todos os sistemas, com governança, resiliência e observabilidade.

## A ideia em uma frase

Um proxy compatível com a API da OpenAI que qualquer projeto usa trocando só a `base_url`. Por trás dele:

- autenticação por cliente;
- mascaramento de dados pessoais;
- cache;
- fallback entre provedores;
- contabilidade de custo;
- métricas e traces.

## Usuários

| Persona | O que ganha |
|---|---|
| Desenvolvedor de um sistema cliente | Um endpoint só, compatível com o SDK da OpenAI, com fallback e cache sem escrever código |
| Time de plataforma | Controle de chaves, limites e cotas por sistema; visão de saúde dos provedores |
| Gestor / FinOps | Custo por sistema e por modelo, em tempo real |
| Segurança / DPO | Garantia de que CPF, CNPJ, e-mail, telefone e cartão não saem da empresa e não ficam em log |

## Escopo

**Dentro**

- `POST /v1/chat/completions` (com e sem streaming) e `GET /v1/models`, no formato da OpenAI, preservando campos que o gateway não conhece (tools, response_format etc.).
- Provedores: OpenAI, Ollama (ou qualquer API compatível) e um provedor simulado para testes e demonstração.
- Aliases de modelo com cadeia de fallback, retry e circuit breaker.
- Chave, rate limit e cota mensal de tokens por cliente.
- Mascaramento reversível de dados pessoais brasileiros, incluindo o CNPJ alfanumérico.
- Cache de respostas no Redis.
- Custo por requisição a partir de uma tabela de preços.
- Métricas e traces com OpenTelemetry, dashboard no Grafana.
- Painel administrativo em Angular e um script de carga para gerar tráfego realista.

**Fora (por enquanto)**

- Embeddings, imagens e áudio.
- Cache semântico por similaridade de embeddings.
- Login no painel (OIDC) e multi-tenant.
- Rate limit e circuit breaker distribuídos entre várias réplicas.

## Requisitos não funcionais

| Requisito | Como é atendido |
|---|---|
| Compatibilidade | Mesmo contrato da OpenAI; o corpo é tratado como JSON genérico, então campos novos passam sem mudança de código |
| Privacidade | PII mascarada antes do provedor, mapa só em memória, logs sempre mascarados |
| Resiliência | Retry com jitter, circuit breaker por provedor, fallback por alias |
| Baixo overhead | Gravação de uso assíncrona por canal em lote; lookup de cliente com cache |
| Observabilidade | Métricas de negócio (tokens, custo) e técnicas (latência, erros) no mesmo dashboard |
| Reprodutibilidade | Tudo em Docker; provedor mock permite demonstrar sem nenhuma chave paga |

## Critérios de sucesso

- Um projeto existente passa a usar o gateway trocando só a URL e a chave.
- Nenhum CPF de teste aparece no corpo enviado ao provedor nem no banco, comprovado por teste automatizado.
- Com um provedor fora do ar, as requisições continuam respondendo pelo próximo da cadeia.
- O dashboard mostra custo por cliente e latência p95 alguns segundos depois do tráfego.
