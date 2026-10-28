import http from 'k6/http';
import { check, fail, sleep } from 'k6';

const gateway = __ENV.GATEWAY_URL || 'http://localhost:8300';
const admin = __ENV.ADMIN_URL || 'http://localhost:8380';
const prometheus = __ENV.PROMETHEUS_URL || 'http://localhost:9090';
const grafana = __ENV.GRAFANA_URL || 'http://localhost:8330';
const jaeger = __ENV.JAEGER_URL || 'http://localhost:16686';
const key = __ENV.CLIENT_KEY || 'gw_demo_playground';
const sandboxKey = __ENV.SANDBOX_KEY || 'gw_demo_sandbox';

export const options = {
  vus: 1,
  iterations: 1,
  thresholds: { checks: ['rate==1'] },
};

function chat(model, content, extra) {
  const settings = extra || {};
  return http.post(
    `${gateway}/v1/chat/completions`,
    JSON.stringify({ model, stream: !!settings.stream, messages: [{ role: 'user', content }] }),
    {
      headers: Object.assign(
        { 'Content-Type': 'application/json', Authorization: `Bearer ${settings.key || key}` },
        settings.headers || {},
      ),
    },
  );
}

function answer(response) {
  return response.json('choices.0.message.content') || '';
}

function waitFor(description, probe, attempts) {
  for (let i = 0; i < (attempts || 30); i++) {
    if (probe()) {
      return true;
    }
    sleep(2);
  }
  fail(`Tempo esgotado esperando: ${description}`);
  return false;
}

export default function () {
  check(http.get(`${gateway}/health`), { 'health 200': (r) => r.status === 200 });

  const anonymous = http.get(`${gateway}/v1/models`);
  check(anonymous, {
    'sem chave retorna 401': (r) => r.status === 401,
    'erro no formato OpenAI': (r) => r.json('error.code') === 'missing_api_key',
  });

  const models = http.get(`${gateway}/v1/models`, { headers: { Authorization: `Bearer ${key}` } });
  check(models, { 'lista o alias mock-1': (r) => (r.json('data.#.id') || []).includes('mock-1') });

  const masked = chat('mock-1', 'CPF 529.982.247-25 e e-mail ana@empresa.com', {
    headers: { 'x-gateway-pii-rehydrate': 'false', 'x-gateway-cache': 'bypass' },
  });
  check(masked, {
    'provedor recebeu placeholders': (r) => answer(r).includes('[CPF_1]') && answer(r).includes('[EMAIL_1]'),
    'provedor não recebeu o CPF': (r) => !answer(r).includes('529.982.247-25'),
  });

  const restored = chat('mock-1', 'CPF 529.982.247-25 e e-mail ana@empresa.com', {
    headers: { 'x-gateway-cache': 'bypass' },
  });
  check(restored, {
    'resposta restaurada para o cliente': (r) => answer(r).includes('529.982.247-25'),
    'custo informado': (r) => parseFloat(r.headers['X-Gateway-Cost-Usd'] || '0') > 0,
  });

  const streamed = chat('mock-1', 'Stream para bia@empresa.com', { stream: true });
  check(streamed, {
    'stream em text/event-stream': (r) => (r.headers['Content-Type'] || '').startsWith('text/event-stream'),
    'stream termina com [DONE]': (r) => r.body.trim().endsWith('data: [DONE]'),
    'stream restaura o e-mail': (r) => r.body.includes('bia@') && !r.body.includes('[EMAIL_1]'),
  });

  const fallback = chat('default', `Fallback ${Date.now()}`);
  check(fallback, {
    'alias default respondeu': (r) => r.status === 200,
    'caiu no provedor mock': (r) => r.headers['X-Gateway-Provider'] === 'mock',
    'registrou fallbacks': (r) => parseInt(r.headers['X-Gateway-Fallbacks'] || '0', 10) >= 1,
  });

  const cachedPrompt = `Cache ${Date.now()}`;
  chat('mock-1', cachedPrompt);
  check(chat('mock-1', cachedPrompt), { 'segunda chamada vem do cache': (r) => r.headers['X-Gateway-Cache'] === 'hit' });

  const statuses = [];
  for (let i = 0; i < 7; i++) {
    statuses.push(chat('mock-1', `Limite ${i} ${Date.now()}`, { key: sandboxKey }).status);
  }
  check(statuses, { 'rate limit devolve 429': (s) => s.includes(429) });

  check(http.get(`${admin}/admin-api/providers`), {
    'painel acessa a API admin pelo nginx': (r) => r.status === 200 && (r.json('#') || 0) > 0,
  });
  check(http.get(`${admin}/`), { 'painel Angular servido': (r) => r.status === 200 && r.body.includes('<app-root>') });

  waitFor('métricas no Prometheus', () => {
    const r = http.get(`${prometheus}/api/v1/query?query=${encodeURIComponent('sum(llm_gateway_requests_total)')}`);
    return r.status === 200 && (r.json('data.result') || []).length > 0;
  });
  const statusesMetric = http.get(
    `${prometheus}/api/v1/query?query=${encodeURIComponent('sum by (status) (llm_gateway_requests_total)')}`,
  );
  check(statusesMetric, {
    'Prometheus vê requisições ok e rate_limited': (r) => {
      const labels = (r.json('data.result') || []).map((item) => item.metric.status);
      return labels.includes('ok') && labels.includes('rate_limited');
    },
  });

  waitFor('Grafana aceitando conexões', () => http.get(`${grafana}/api/health`).status === 200);
  check(http.get(`${grafana}/api/dashboards/uid/llm-gateway`), {
    'dashboard provisionado no Grafana': (r) => r.status === 200 && r.json('dashboard.title') === 'LLM Gateway',
  });
  check(
    http.post(
      `${grafana}/api/ds/query`,
      JSON.stringify({
        from: 'now-15m',
        to: 'now',
        queries: [{
          refId: 'A',
          datasource: { type: 'prometheus', uid: 'prometheus' },
          expr: 'sum(increase(llm_gateway_requests_total[15m]))',
          instant: true,
        }],
      }),
      { headers: { 'Content-Type': 'application/json' } },
    ),
    { 'Grafana consulta o Prometheus pelo datasource provisionado': (r) => r.status === 200 && r.body.includes('"values"') },
  );

  waitFor('traces no Jaeger', () => {
    const r = http.get(`${jaeger}/api/services`);
    return r.status === 200 && (r.json('data') || []).includes('llm-gateway');
  });
}
