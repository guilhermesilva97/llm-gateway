export interface ClientView {
  id: string;
  name: string;
  keyPreview: string;
  requestsPerMinute: number;
  monthlyTokenQuota: number;
  tokensUsedThisMonth: number;
  maskPii: boolean;
  active: boolean;
  createdAt: string;
}

export interface CreatedClient {
  client: ClientView;
  apiKey: string;
}

export interface NewClient {
  name: string;
  requestsPerMinute: number;
  monthlyTokenQuota: number;
  maskPii: boolean;
}

export interface ClientPatch {
  name?: string;
  requestsPerMinute?: number;
  monthlyTokenQuota?: number;
  maskPii?: boolean;
  active?: boolean;
}

export interface ClientUsage {
  clientId: string;
  requests: number;
  errors: number;
  cacheHits: number;
  promptTokens: number;
  completionTokens: number;
  costUsd: number;
  averageLatencyMs: number;
  fallbacks: number;
}

export interface UsagePoint {
  hour: string;
  requests: number;
  errors: number;
  costUsd: number;
}

export interface UsageOverview {
  hours: number;
  totals: ClientUsage;
  clients: ClientUsage[];
  timeline: UsagePoint[];
}

export interface RequestView {
  requestId: string;
  clientId: string;
  timestamp: string;
  alias: string;
  provider: string | null;
  model: string | null;
  stream: boolean;
  promptTokens: number;
  completionTokens: number;
  costUsd: number;
  latencyMs: number;
  status: string;
  httpStatus: number;
  cacheHit: boolean;
  fallbacks: number;
  piiMasked: number;
  promptPreview: string;
  error: string | null;
}

export interface ProviderStatus {
  name: string;
  type: string;
  configured: boolean;
  circuit: 'closed' | 'half-open' | 'open';
  successes: number;
  failures: number;
  lastLatencyMs: number | null;
  lastSuccessAt: string | null;
  lastFailureAt: string | null;
  lastError: string | null;
}

export interface ModelPrice {
  inputPerMillion: number;
  outputPerMillion: number;
}

export interface ModelRouteView {
  alias: string;
  targets: { target: string; price: ModelPrice | null }[];
}
