# ADR 0002: mascaramento de dados pessoais reversível por requisição

**Status:** aceita

## Contexto

Os projetos mandam ao LLM textos de usuários reais: CPF em atendimento, e-mail em documento, cartão colado por engano. Enviar isso a um provedor externo é um risco de LGPD, e gravar isso em log é outro.

Apagar o dado (`***`) quebra a utilidade da resposta: se o usuário pede "confirme meu e-mail", o modelo precisa devolver *algo* no lugar certo.

## Decisão

- Antes de chamar o provedor, o gateway troca cada dado pessoal por um **placeholder numerado**, como `[CPF_1]`, `[EMAIL_2]` ou `[TELEFONE_1]`.
- O mapa placeholder → valor original vive apenas na memória da requisição (`PiiVault`). Ele nunca é persistido nem logado.
- Na resposta, os placeholders são trocados de volta pelos valores originais. Isso é feito também em **streaming**, com um buffer que segura apenas o trecho que ainda pode ser um placeholder incompleto (`[EMAI` …).
- O cliente pode desligar a restauração (`x-gateway-pii-rehydrate: false`) ou o mascaramento inteiro, por configuração do cliente.
- O log de requisições guarda sempre a versão mascarada, mesmo para clientes que desligaram o mascaramento.

Para reduzir falsos positivos:

| Dado | Regra |
|---|---|
| CPF e CNPJ formatados (com pontuação) | Sempre mascarados |
| CPF e CNPJ sem pontuação | Mascarados só se o dígito verificador for válido |
| CNPJ alfanumérico | Suportado, com validação dos dígitos (formato em vigor desde julho de 2026) |
| Cartão | Validado pelo algoritmo de Luhn |
| Telefone | Exige DDD entre parênteses, `+55` ou separador, para não confundir com números soltos |

## Consequências

- O provedor nunca recebe o dado real, e a resposta continua útil para o usuário.
- **Efeito colateral positivo no cache:** duas perguntas iguais com e-mails diferentes geram o mesmo texto mascarado e compartilham a mesma entrada de cache. Cada uma é restaurada com o próprio e-mail.
- Detecção por regex não pega tudo: nomes, endereços e dados em imagens passam. Um detector baseado em NER (reconhecimento de entidades) fica como próximo passo.
- Se o modelo alterar o placeholder (por exemplo, `[CPF 1]`), ele não é restaurado. Na prática, os modelos testados preservam o formato entre colchetes.
