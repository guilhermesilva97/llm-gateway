import { SseMessage, createSseParser } from './sse';

function collect(chunks: string[]): SseMessage[] {
  const messages: SseMessage[] = [];
  const feed = createSseParser((message) => messages.push(message));
  chunks.forEach(feed);
  return messages;
}

describe('createSseParser', () => {
  it('parses OpenAI style data events', () => {
    const messages = collect(['data: {"a":1}\n\ndata: [DONE]\n\n']);
    expect(messages.map((m) => m.data)).toEqual(['{"a":1}', '[DONE]']);
  });

  it('joins events split across network chunks', () => {
    const messages = collect(['data: {"choi', 'ces":[]}\n', '\n']);
    expect(JSON.parse(messages[0].data)).toEqual({ choices: [] });
  });

  it('handles CRLF and ignores comments', () => {
    const messages = collect([': ping\r\n\r\nevent: x\r\ndata: 1\r\n\r\n']);
    expect(messages).toEqual([{ event: 'x', data: '1' }]);
  });
});
