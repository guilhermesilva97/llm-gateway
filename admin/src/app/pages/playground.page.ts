import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApi } from '../core/admin-api';
import { ChatMetadata, sendChat } from '../core/chat-client';
import { ModelRouteView } from '../core/models';

const KEY_STORAGE = 'llm-gateway.playground-key';

@Component({
  selector: 'app-playground',
  imports: [FormsModule],
  template: `
    <header class="page-header">
      <div>
        <h1>Playground</h1>
        <p class="muted">Chame o gateway como um cliente faria. Experimente incluir um CPF ou e-mail na mensagem.</p>
      </div>
    </header>

    <section class="playground">
      <form class="card form-stack" (ngSubmit)="send()">
        <label class="field">
          <span>Chave do cliente</span>
          <input class="input" name="apiKey" type="password" autocomplete="off" [(ngModel)]="apiKey"
            (ngModelChange)="remember($event)" placeholder="gw_..." />
        </label>
        <label class="field">
          <span>Modelo</span>
          <select class="input" name="model" [(ngModel)]="model">
            @for (route of models(); track route.alias) {
              <option [value]="route.alias">{{ route.alias }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span>Mensagem</span>
          <textarea class="input" name="message" rows="5" [(ngModel)]="message"></textarea>
        </label>
        <div class="toggles">
          <label class="field field--inline"><input type="checkbox" name="stream" [(ngModel)]="stream" /><span>Streaming</span></label>
          <label class="field field--inline"><input type="checkbox" name="rehydrate" [(ngModel)]="rehydrate" /><span>Restaurar dados pessoais na resposta</span></label>
          <label class="field field--inline"><input type="checkbox" name="bypass" [(ngModel)]="bypassCache" /><span>Ignorar cache</span></label>
        </div>
        <button class="btn" type="submit" [disabled]="running() || !apiKey || !message.trim()">
          {{ running() ? 'Aguardando...' : 'Enviar' }}
        </button>
      </form>

      <article class="card output" aria-live="polite">
        <h2 class="card__title">Resposta</h2>
        @if (error()) {
          <div class="alert">{{ error() }}</div>
        }
        <pre class="output__text">{{ answer() || '—' }}</pre>
        @if (metadata(); as meta) {
          <dl class="stats">
            <div><dt>Destino</dt><dd>{{ meta.provider }}/{{ meta.model }}</dd></div>
            <div><dt>Cache</dt><dd>{{ meta.cache }}</dd></div>
            <div><dt>Fallbacks</dt><dd>{{ meta.fallbacks }}</dd></div>
            <div><dt>Custo</dt><dd>{{ meta.costUsd ? 'US$ ' + meta.costUsd : 'calculado ao fim do stream' }}</dd></div>
            <div><dt>Request ID</dt><dd><code>{{ meta.requestId }}</code></dd></div>
          </dl>
        }
      </article>
    </section>
  `,
})
export class PlaygroundPage {
  private readonly api = inject(AdminApi);

  protected readonly models = signal<ModelRouteView[]>([]);
  protected readonly answer = signal('');
  protected readonly metadata = signal<ChatMetadata | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly running = signal(false);

  protected apiKey = PlaygroundPage.readKey();
  protected model = 'mock-1';
  protected message = 'Meu nome é Ana, CPF 529.982.247-25, e-mail ana.silva@empresa.com.br. Pode confirmar meus dados?';
  protected stream = true;
  protected rehydrate = true;
  protected bypassCache = false;

  constructor() {
    void this.api
      .models()
      .then((models) => this.models.set(models))
      .catch(() => this.models.set([{ alias: 'mock-1', targets: [] }]));
  }

  protected remember(key: string): void {
    try {
      localStorage.setItem(KEY_STORAGE, key);
    } catch {
      return;
    }
  }

  protected async send(): Promise<void> {
    this.running.set(true);
    this.answer.set('');
    this.metadata.set(null);
    this.error.set(null);
    try {
      const metadata = await sendChat(
        {
          apiKey: this.apiKey,
          model: this.model,
          message: this.message,
          stream: this.stream,
          rehydratePii: this.rehydrate,
          bypassCache: this.bypassCache,
        },
        (text) => this.answer.update((current) => current + text),
      );
      this.metadata.set(metadata);
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Falha ao chamar o gateway.');
    } finally {
      this.running.set(false);
    }
  }

  private static readKey(): string {
    try {
      return localStorage.getItem(KEY_STORAGE) ?? '';
    } catch {
      return '';
    }
  }
}
