import http from 'k6/http';
import { check, sleep } from 'k6';

const gateway = __ENV.GATEWAY_URL || 'http://localhost:8300';
const keys = (__ENV.KEYS || 'gw_demo_insightql').split(',');
const sandboxKey = __ENV.SANDBOX_KEY || 'gw_demo_sandbox';
const duration = __ENV.DURATION || '3m';

const prompts = [
  'Resuma em uma frase o pedido 4521.',
  'Qual o status do contrato do cliente com CPF 529.982.247-25?',
  'Envie a nota fiscal para financeiro@empresa.com.br.',
  'A empresa 11.222.333/0001-81 está adimplente?',
  'Ligue para (11) 98765-4321 e confirme a entrega.',
  'Classifique o sentimento: o atendimento foi excelente.',
  'Liste três riscos de migrar o banco para a nuvem.',
];

const models = ['mock-1', 'mock-1', 'mock-resilient', 'default'];

export const options = {
  scenarios: {
    clients: {
      executor: 'ramping-arrival-rate',
      startRate: 2,
      timeUnit: '1s',
      preAllocatedVUs: 20,
      maxVUs: 60,
      stages: [
        { target: 4, duration: '30s' },
        { target: 6, duration: duration },
        { target: 0, duration: '20s' },
      ],
      exec: 'chat',
    },
    sandbox: {
      executor: 'constant-arrival-rate',
      rate: 12,
      timeUnit: '1m',
      duration: duration,
      preAllocatedVUs: 2,
      exec: 'sandbox',
    },
  },
  thresholds: {
    checks: ['rate>0.85'],
  },
};

function pick(items) {
  return items[Math.floor(Math.random() * items.length)];
}

function call(key, body, extraHeaders) {
  return http.post(`${gateway}/v1/chat/completions`, JSON.stringify(body), {
    headers: Object.assign(
      { 'Content-Type': 'application/json', Authorization: `Bearer ${key}` },
      extraHeaders || {},
    ),
    tags: { model: body.model },
  });
}

export function chat() {
  const stream = Math.random() < 0.2;
  const response = call(pick(keys), {
    model: pick(models),
    stream,
    messages: [{ role: 'user', content: pick(prompts) }],
  });
  check(response, { 'status 200': (r) => r.status === 200 });
}

export function sandbox() {
  const response = call(sandboxKey, {
    model: 'mock-1',
    messages: [{ role: 'user', content: `Teste de limite ${Date.now()}` }],
  });
  check(response, { 'sandbox 200 ou 429': (r) => r.status === 200 || r.status === 429 });
  sleep(0.1);
}
