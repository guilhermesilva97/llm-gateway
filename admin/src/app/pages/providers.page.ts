import { Component, DestroyRef, inject, signal } from '@angular/core';
import { AdminApi, errorMessage } from '../core/admin-api';
import { formatLatency, formatNumber, formatTime } from '../core/format';
import { ModelRouteView, ProviderStatus } from '../core/models';

@Component({
  selector: 'app-providers',
  template: `
    <header class="page-header">
      <div>
        <h1>Provedores e rotas</h1>
        <p class="muted">Estado do circuit breaker de cada provedor e a cadeia de fallback de cada modelo.</p>
      </div>
    </header>

    @if (error()) {
      <div class="alert">{{ error() }}</div>
    }

    <section class="provider-grid">
      @for (provider of providers(); track provider.name) {
        <article class="card provider">
          <header class="provider__header">
            <h2>{{ provider.name }}</h2>
            <span class="badge" [class.badge--ok]="provider.configured && provider.circuit === 'closed'"
              [class.badge--danger]="provider.circuit === 'open'">{{ circuitLabel(provider) }}</span>
          </header>
          <dl class="stats">
            <div><dt>Tipo</dt><dd>{{ provider.type }}</dd></div>
            <div><dt>Sucessos</dt><dd>{{ fmt.number(provider.successes) }}</dd></div>
            <div><dt>Falhas</dt><dd>{{ fmt.number(provider.failures) }}</dd></div>
            <div><dt>Última latência</dt><dd>{{ fmt.latency(provider.lastLatencyMs) }}</dd></div>
            <div><dt>Último sucesso</dt><dd>{{ fmt.time(provider.lastSuccessAt) }}</dd></div>
            <div><dt>Última falha</dt><dd>{{ fmt.time(provider.lastFailureAt) }}</dd></div>
          </dl>
          @if (provider.lastError) {
            <p class="provider__error">{{ provider.lastError }}</p>
          }
        </article>
      }
    </section>

    <section class="card">
      <h2 class="card__title">Modelos expostos</h2>
      <div class="table-wrapper">
        <table class="table">
          <thead><tr><th>Alias</th><th>Cadeia de destinos (em ordem)</th></tr></thead>
          <tbody>
            @for (route of models(); track route.alias) {
              <tr>
                <td><code>{{ route.alias }}</code></td>
                <td>
                  <ol class="route">
                    @for (target of route.targets; track target.target) {
                      <li>
                        <code>{{ target.target }}</code>
                        <small class="muted">{{ price(target.price) }}</small>
                      </li>
                    }
                  </ol>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </section>
  `,
})
export class ProvidersPage {
  private readonly api = inject(AdminApi);

  protected readonly fmt = { number: formatNumber, latency: formatLatency, time: formatTime };
  protected readonly providers = signal<ProviderStatus[]>([]);
  protected readonly models = signal<ModelRouteView[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
    const timer = setInterval(() => void this.load(), 5_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected circuitLabel(provider: ProviderStatus): string {
    if (!provider.configured) {
      return 'sem credencial';
    }
    return { closed: 'fechado', 'half-open': 'meio aberto', open: 'aberto' }[provider.circuit];
  }

  protected price(price: ModelRouteView['targets'][number]['price']): string {
    if (!price) {
      return 'sem preço cadastrado';
    }
    if (price.inputPerMillion === 0 && price.outputPerMillion === 0) {
      return 'gratuito';
    }
    return `US$ ${price.inputPerMillion} / US$ ${price.outputPerMillion} por 1M tokens (entrada / saída)`;
  }

  private async load(): Promise<void> {
    try {
      const [providers, models] = await Promise.all([this.api.providers(), this.api.models()]);
      this.providers.set(providers);
      this.models.set(models);
      this.error.set(null);
    } catch (error) {
      this.error.set(errorMessage(error));
    }
  }
}
