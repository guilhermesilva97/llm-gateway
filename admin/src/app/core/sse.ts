export interface SseMessage {
  event: string;
  data: string;
}

export function createSseParser(onMessage: (message: SseMessage) => void): (chunk: string) => void {
  let buffer = '';

  return (chunk: string) => {
    buffer += chunk.replace(/\r\n/g, '\n');
    let boundary = buffer.indexOf('\n\n');
    while (boundary !== -1) {
      const block = buffer.slice(0, boundary);
      buffer = buffer.slice(boundary + 2);
      const message = parseBlock(block);
      if (message) {
        onMessage(message);
      }
      boundary = buffer.indexOf('\n\n');
    }
  };
}

function parseBlock(block: string): SseMessage | null {
  let event = 'message';
  const data: string[] = [];
  for (const line of block.split('\n')) {
    if (line.startsWith('event:')) {
      event = line.slice(6).trim();
    } else if (line.startsWith('data:')) {
      data.push(line.slice(5).trimStart());
    }
  }
  return data.length ? { event, data: data.join('\n') } : null;
}
