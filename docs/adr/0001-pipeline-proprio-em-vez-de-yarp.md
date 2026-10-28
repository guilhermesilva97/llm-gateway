# ADR 0001: pipeline próprio em vez de YARP para o tráfego de chat

**Status:** aceita

## Contexto

O gateway fica entre os projetos e os provedores de LLM. A primeira ideia foi usar o YARP, o reverse proxy da Microsoft, que já resolve encaminhamento, balanceamento, health check e streaming com ótima performance.

O problema é o que o gateway precisa fazer com cada requisição:

- **ler e alterar o corpo:** mascarar dados pessoais e trocar o alias pelo modelo real;
- **ler e alterar a resposta:** restaurar os dados pessoais, inclusive no meio de um streaming SSE;
- **trocar de destino** no meio do caminho quando um provedor falha;
- **contar tokens** do bloco final de uso do stream e calcular custo;
- **responder sem chamar ninguém** quando há cache.

O YARP foi desenhado para encaminhar bytes com o mínimo de interferência. Ele tem *transforms* de cabeçalho e caminho, mas reescrever o corpo, e principalmente o corpo de um stream, exige sair do modelo dele e bufferizar manualmente. Fallback com corpo alterado entre tentativas também não é o caso de uso dele.

## Decisão

O tráfego de `/v1/chat/completions` passa por um **pipeline próprio** no caso de uso `ChatGateway`:

- `HttpClient` via `IHttpClientFactory`;
- Polly para retry e circuit breaker;
- leitura do SSE linha a linha com `IAsyncEnumerable`.

O YARP fica registrado como alternativa para um passthrough puro, por exemplo `/v1/embeddings` sem mascaramento, caso esse endpoint seja adicionado.

## Consequências

**Positivas**

- Controle total sobre corpo, stream, fallback e contabilidade.
- O pipeline é testável sem rede, com provedores falsos, em testes unitários.
- Um único lugar concentra as regras: autenticação, cota, PII, cache, roteamento e custo.

**Negativas**

- Escrevi código que o YARP já teria pronto: parsing de SSE, timeout e cabeçalhos.
- Performance de proxy puro inferior à do YARP. Aqui ela é irrelevante: a latência dominante é a do LLM, de centenas de milissegundos a segundos.
