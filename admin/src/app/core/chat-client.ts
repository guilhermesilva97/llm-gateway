import { createSseParser } from './sse';

export interface ChatRequest {
  apiKey: string;
  model: string;
  message: string;
  stream: boolean;
  rehydratePii: boolean;
  bypassCache: boolean;
}

export interface ChatMetadata {
  status: number;
  provider: string | null;
  model: string | null;
  cache: string | null;
  fallbacks: string | null;
  costUsd: string | null;
  requestId: string | null;
}

export class ChatError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
  }
}

export async function sendChat(
  request: ChatRequest,
  onText: (text: string) => void,
  signal?: AbortSignal,
): Promise<ChatMetadata> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    Authorization: `Bearer ${request.apiKey}`,
    'x-gateway-pii-rehydrate': String(request.rehydratePii),
  };
  if (request.bypassCache) {
    headers['x-gateway-cache'] = 'bypass';
  }

  const response = await fetch('/v1/chat/completions', {
    method: 'POST',
    headers,
    body: JSON.stringify({
      model: request.model,
      stream: request.stream,
      messages: [{ role: 'user', content: request.message }],
    }),
    signal,
  });

  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new ChatError(body?.error?.message ?? `HTTP ${response.status}`, response.status);
  }

  const metadata = readMetadata(response);
  if (!request.stream) {
    const body = await response.json();
    onText(body?.choices?.[0]?.message?.content ?? '');
    return metadata;
  }

  const reader = response.body!.pipeThrough(new TextDecoderStream()).getReader();
  const feed = createSseParser(({ data }) => {
    if (data === '[DONE]') {
      return;
    }
    const chunk = JSON.parse(data);
    if (chunk.error) {
      throw new ChatError(chunk.error.message, 502);
    }
    const text = chunk.choices?.[0]?.delta?.content;
    if (typeof text === 'string' && text.length > 0) {
      onText(text);
    }
  });
  for (;;) {
    const { value, done } = await reader.read();
    if (done) {
      break;
    }
    feed(value);
  }
  return metadata;
}

function readMetadata(response: Response): ChatMetadata {
  const header = (name: string) => response.headers.get(name);
  return {
    status: response.status,
    provider: header('x-gateway-provider'),
    model: header('x-gateway-model'),
    cache: header('x-gateway-cache'),
    fallbacks: header('x-gateway-fallbacks'),
    costUsd: header('x-gateway-cost-usd'),
    requestId: header('x-request-id'),
  };
}
