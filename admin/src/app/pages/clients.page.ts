import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApi, errorMessage } from '../core/admin-api';
import { formatNumber, formatTime, quotaShare } from '../core/format';
import { ClientView, CreatedClient, NewClient } from '../core/models';

@Component({
  selector: 'app-clients',
  imports: [FormsModule],
  template: `
    <header class="page-header">
      <div>
        <h1>Clientes</h1>
        <p class="muted">Cada projeto do portfólio é um cliente com chave, limite por minuto e cota mensal próprios.</p>
      </div>
    </header>

    @if (error()) {
      <div class="alert">{{ error() }}</div>
    }

    @if (revealed(); as created) {
      <div class="callout" role="status">
        <strong>Chave de {{ created.client.name }}</strong>
        <p>Copie agora: ela não será exibida de novo. O gateway guarda apenas o hash SHA-256.</p>
        <code class="secret">{{ created.apiKey }}</code>
        <button type="button" class="btn btn--ghost" (click)="revealed.set(null)">Entendi</button>
      </div>
    }

    <section class="card">
      <h2 class="card__title">Novo cliente</h2>
      <form class="form-grid" (ngSubmit)="create()">
        <label class="field">
          <span>Nome</span>
          <input class="input" name="name" [(ngModel)]="draft.name" required maxlength="120" />
        </label>
        <label class="field">
          <span>Requisições por minuto</span>
          <input class="input" name="rpm" type="number" min="1" [(ngModel)]="draft.requestsPerMinute" />
        </label>
        <label class="field">
          <span>Cota mensal de tokens</span>
          <input class="input" name="quota" type="number" min="1" [(ngModel)]="draft.monthlyTokenQuota" />
        </label>
        <label class="field field--inline">
          <input name="maskPii" type="checkbox" [(ngModel)]="draft.maskPii" />
          <span>Mascarar dados pessoais</span>
        </label>
        <button class="btn" type="submit" [disabled]="busy() || !draft.name.trim()">Criar cliente</button>
      </form>
    </section>

    <section class="card">
      <h2 class="card__title">Clientes cadastrados</h2>
      <div class="table-wrapper">
        <table class="table">
          <thead>
            <tr>
              <th>Cliente</th><th>Chave</th><th class="num">RPM</th><th>Cota do mês</th>
              <th>PII</th><th>Status</th><th>Criado em</th><th></th>
            </tr>
          </thead>
          <tbody>
            @for (client of clients(); track client.id) {
              <tr [class.row--inactive]="!client.active">
                <td><strong>{{ client.name }}</strong><br /><small class="muted">{{ client.id }}</small></td>
                <td><code>{{ client.keyPreview }}</code></td>
                <td class="num">{{ client.requestsPerMinute }}</td>
                <td>
                  <div class="meter" [attr.aria-label]="share(client) + '% da cota usada'">
                    <div class="meter__fill" [class.meter__fill--warn]="share(client) >= 80" [style.width.%]="share(client)"></div>
                  </div>
                  <small class="muted">{{ fmt.number(client.tokensUsedThisMonth) }} / {{ fmt.number(client.monthlyTokenQuota) }}</small>
                </td>
                <td>{{ client.maskPii ? 'mascarado' : 'original' }}</td>
                <td><span class="badge" [class.badge--ok]="client.active">{{ client.active ? 'ativo' : 'inativo' }}</span></td>
                <td>{{ fmt.time(client.createdAt) }}</td>
                <td class="actions">
                  <button type="button" class="btn btn--ghost" [disabled]="busy()" (click)="toggle(client)">
                    {{ client.active ? 'Desativar' : 'Ativar' }}
                  </button>
                  <button type="button" class="btn btn--ghost" [disabled]="busy()" (click)="rotate(client)">Nova chave</button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </section>
  `,
})
export class ClientsPage {
  private readonly api = inject(AdminApi);

  protected readonly fmt = { number: formatNumber, time: formatTime };
  protected readonly clients = signal<ClientView[]>([]);
  protected readonly revealed = signal<CreatedClient | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected draft: NewClient = ClientsPage.emptyDraft();

  constructor() {
    void this.reload();
  }

  protected share(client: ClientView): number {
    return quotaShare(client.tokensUsedThisMonth, client.monthlyTokenQuota);
  }

  protected async create(): Promise<void> {
    await this.run(async () => {
      this.revealed.set(await this.api.createClient(this.draft));
      this.draft = ClientsPage.emptyDraft();
    });
  }

  protected async toggle(client: ClientView): Promise<void> {
    await this.run(() => this.api.updateClient(client.id, { active: !client.active }));
  }

  protected async rotate(client: ClientView): Promise<void> {
    if (!confirm(`Gerar nova chave para ${client.name}? A chave atual deixa de funcionar.`)) {
      return;
    }
    await this.run(async () => this.revealed.set(await this.api.rotateKey(client.id)));
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
      await this.reload();
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async reload(): Promise<void> {
    try {
      this.clients.set(await this.api.clients());
      this.error.set(null);
    } catch (error) {
      this.error.set(errorMessage(error));
    }
  }

  private static emptyDraft(): NewClient {
    return { name: '', requestsPerMinute: 60, monthlyTokenQuota: 1_000_000, maskPii: true };
  }
}
