import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { AdminApi, errorMessage } from '../core/admin-api';
import { formatLatency, formatNumber, formatPercent, formatUsd } from '../core/format';
import { ProviderStatus, UsageOverview } from '../core/models';

@Component({
  selector: 'app-overview',
  template: `
    <header class="page-header">
      <div>
        <h1>Visão geral</h1>
        <p class="muted">Consumo, custo e saúde do gateway. Atualiza a cada 10 segundos.</p>
      </div>
      <div class="segmented" role="group" aria-label="Período">
        @for (option of periods; track option.hours) {
          <button type="button" [class.segmented__item--active]="hours() === option.hours" class="segmented__item"
            (click)="hours.set(option.hours)">{{ option.label }}</button>
        }
      </div>
    </header>

    @if (error()) {
      <div class="alert">{{ error() }}</div>
    }

    @if (overview(); as data) {
      <section class="kpis">
        <article class="card kpi"><span class="kpi__label">Requisições</span><strong class="kpi__value">{{ fmt.number(data.totals.requests) }}</strong></article>
        <article class="card kpi"><span class="kpi__label">Custo estimado</span><strong class="kpi__value">{{ fmt.usd(data.totals.costUsd) }}</strong></article>
        <article class="card kpi"><span class="kpi__label">Tokens</span><strong class="kpi__value">{{ fmt.number(data.totals.promptTokens + data.totals.completionTokens) }}</strong></article>
        <article class="card kpi"><span class="kpi__label">Cache hit</span><strong class="kpi__value">{{ fmt.percent(data.totals.cacheHits, data.totals.requests) }}</strong></article>
        <article class="card kpi"><span class="kpi__label">Taxa de erro</span><strong class="kpi__value">{{ fmt.percent(data.totals.errors, data.totals.requests) }}</strong></article>
        <article class="card kpi"><span class="kpi__label">Latência média</span><strong class="kpi__value">{{ fmt.latency(data.totals.averageLatencyMs) }}</strong></article>
      </section>

      <section class="card">
        <h2 class="card__title">Requisições por hora</h2>
        @if (data.timeline.length === 0) {
          <p class="muted">Nenhuma requisição no período.</p>
        } @else {
          <div class="bars" role="img" [attr.aria-label]="'Requisições por hora nas últimas ' + data.hours + ' horas'">
            @for (point of data.timeline; track point.hour) {
              <div class="bars__item" [title]="hourLabel(point.hour) + ': ' + point.requests + ' requisições, ' + point.errors + ' erros'">
                <div class="bars__bar" [style.height.%]="(point.requests / maxRequests()) * 100">
                  <div class="bars__errors" [style.height.%]="point.requests ? (point.errors / point.requests) * 100 : 0"></div>
                </div>
                <span class="bars__label">{{ hourLabel(point.hour) }}</span>
              </div>
            }
          </div>
          <p class="legend"><span class="legend__swatch"></span>sucesso <span class="legend__swatch legend__swatch--error"></span>erro</p>
        }
      </section>

      <section class="card">
        <h2 class="card__title">Consumo por cliente</h2>
        <div class="table-wrapper">
          <table class="table">
            <thead>
              <tr>
                <th>Cliente</th><th class="num">Requisições</th><th class="num">Tokens</th><th class="num">Custo</th>
                <th class="num">Cache hit</th><th class="num">Erros</th><th class="num">Fallbacks</th><th class="num">Latência</th>
              </tr>
            </thead>
            <tbody>
              @for (client of data.clients; track client.clientId) {
                <tr>
                  <td>{{ client.clientId }}</td>
                  <td class="num">{{ fmt.number(client.requests) }}</td>
                  <td class="num">{{ fmt.number(client.promptTokens + client.completionTokens) }}</td>
                  <td class="num">{{ fmt.usd(client.costUsd) }}</td>
                  <td class="num">{{ fmt.percent(client.cacheHits, client.requests) }}</td>
                  <td class="num">{{ fmt.number(client.errors) }}</td>
                  <td class="num">{{ fmt.number(client.fallbacks) }}</td>
                  <td class="num">{{ fmt.latency(client.averageLatencyMs) }}</td>
                </tr>
              } @empty {
                <tr><td colspan="8" class="muted">Nenhum consumo no período.</td></tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    }

    <section class="card">
      <h2 class="card__title">Provedores</h2>
      <div class="chips">
        @for (provider of providers(); track provider.name) {
          <span class="chip" [class]="'chip chip--' + chipState(provider)">
            {{ provider.name }} · {{ chipLabel(provider) }}
          </span>
        }
      </div>
    </section>
  `,
})
export class OverviewPage {
  private readonly api = inject(AdminApi);

  protected readonly fmt = {
    number: formatNumber,
    usd: formatUsd,
    percent: formatPercent,
    latency: formatLatency,
  };

  protected readonly periods = [
    { hours: 1, label: '1 h' },
    { hours: 24, label: '24 h' },
    { hours: 168, label: '7 dias' },
  ];

  protected readonly hours = signal(24);
  protected readonly overview = signal<UsageOverview | null>(null);
  protected readonly providers = signal<ProviderStatus[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly maxRequests = computed(() =>
    Math.max(1, ...(this.overview()?.timeline ?? []).map((point) => point.requests)),
  );

  constructor() {
    effect(() => {
      void this.load(this.hours());
    });
    const timer = setInterval(() => void this.load(this.hours()), 10_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected hourLabel(iso: string): string {
    return new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
  }

  protected chipState(provider: ProviderStatus): string {
    if (!provider.configured) {
      return 'muted';
    }
    return provider.circuit === 'closed' ? 'ok' : provider.circuit === 'open' ? 'danger' : 'warn';
  }

  protected chipLabel(provider: ProviderStatus): string {
    if (!provider.configured) {
      return 'sem credencial';
    }
    return { closed: 'saudável', 'half-open': 'testando', open: 'circuito aberto' }[provider.circuit];
  }

  private async load(hours: number): Promise<void> {
    try {
      const [overview, providers] = await Promise.all([this.api.overview(hours), this.api.providers()]);
      this.overview.set(overview);
      this.providers.set(providers);
      this.error.set(null);
    } catch (error) {
      this.error.set(errorMessage(error));
    }
  }
}
