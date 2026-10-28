import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApi, errorMessage } from '../core/admin-api';
import { formatLatency, formatNumber, formatTime, formatUsd } from '../core/format';
import { ClientView, RequestView } from '../core/models';

@Component({
  selector: 'app-requests',
  imports: [FormsModule],
  template: `
    <header class="page-header">
      <div>
        <h1>Requisições</h1>
        <p class="muted">Últimas chamadas. O texto do prompt é armazenado já mascarado.</p>
      </div>
      <div class="toolbar">
        <label class="field field--inline">
          <span>Cliente</span>
          <select class="input" [ngModel]="clientId()" (ngModelChange)="clientId.set($event); refresh()">
            <option [ngValue]="null">Todos</option>
            @for (client of clients(); track client.id) {
              <option [ngValue]="client.id">{{ client.name }}</option>
            }
          </select>
        </label>
        <label class="field field--inline">
          <input type="checkbox" [ngModel]="live()" (ngModelChange)="live.set($event)" />
          <span>Ao vivo</span>
        </label>
      </div>
    </header>

    @if (error()) {
      <div class="alert">{{ error() }}</div>
    }

    <section class="card">
      <div class="table-wrapper">
        <table class="table table--dense">
          <thead>
            <tr>
              <th>Horário</th><th>Cliente</th><th>Modelo</th><th>Destino</th><th>Status</th>
              <th class="num">Tokens</th><th class="num">Custo</th><th class="num">Latência</th><th>Sinais</th><th>Prompt (mascarado)</th>
            </tr>
          </thead>
          <tbody>
            @for (request of requests(); track request.requestId + request.timestamp) {
              <tr>
                <td>{{ fmt.time(request.timestamp) }}</td>
                <td>{{ request.clientId }}</td>
                <td>{{ request.alias }}</td>
                <td>{{ request.provider ?? '—' }}{{ request.model ? '/' + request.model : '' }}</td>
                <td>
                  <span class="badge" [class.badge--ok]="request.httpStatus < 400" [class.badge--danger]="request.httpStatus >= 500"
                    [title]="request.error ?? ''">{{ request.httpStatus }} {{ request.status }}</span>
                </td>
                <td class="num">{{ fmt.number(request.promptTokens + request.completionTokens) }}</td>
                <td class="num">{{ fmt.usd(request.costUsd) }}</td>
                <td class="num">{{ fmt.latency(request.latencyMs) }}</td>
                <td class="signals">
                  @if (request.cacheHit) { <span class="tag">cache</span> }
                  @if (request.stream) { <span class="tag">stream</span> }
                  @if (request.fallbacks > 0) { <span class="tag tag--warn">fallback ×{{ request.fallbacks }}</span> }
                  @if (request.piiMasked > 0) { <span class="tag tag--info">PII ×{{ request.piiMasked }}</span> }
                </td>
                <td class="preview" [title]="request.promptPreview">{{ request.promptPreview || '—' }}</td>
              </tr>
            } @empty {
              <tr><td colspan="10" class="muted">Nenhuma requisição registrada ainda.</td></tr>
            }
          </tbody>
        </table>
      </div>
    </section>
  `,
})
export class RequestsPage {
  private readonly api = inject(AdminApi);

  protected readonly fmt = { number: formatNumber, usd: formatUsd, latency: formatLatency, time: formatTime };
  protected readonly requests = signal<RequestView[]>([]);
  protected readonly clients = signal<ClientView[]>([]);
  protected readonly clientId = signal<string | null>(null);
  protected readonly live = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.api.clients().then((clients) => this.clients.set(clients)).catch(() => undefined);
    void this.refresh();
    const timer = setInterval(() => {
      if (this.live()) {
        void this.refresh();
      }
    }, 5_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected async refresh(): Promise<void> {
    try {
      this.requests.set(await this.api.requests(100, this.clientId()));
      this.error.set(null);
    } catch (error) {
      this.error.set(errorMessage(error));
    }
  }
}
