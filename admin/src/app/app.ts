import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="layout">
      <aside class="sidebar">
        <div class="brand">
          <span class="brand__mark" aria-hidden="true">⇄</span>
          <div>
            <strong>LLM Gateway</strong>
            <small>painel administrativo</small>
          </div>
        </div>
        <nav class="nav" aria-label="Seções">
          @for (link of links; track link.path) {
            <a [routerLink]="link.path" routerLinkActive="nav__link--active" class="nav__link">{{ link.label }}</a>
          }
        </nav>
        <a class="nav__external" [href]="grafanaUrl" target="_blank" rel="noopener">Abrir Grafana ↗</a>
      </aside>
      <main class="content">
        <router-outlet />
      </main>
    </div>
  `,
})
export class App {
  protected readonly links = [
    { path: '/visao-geral', label: 'Visão geral' },
    { path: '/clientes', label: 'Clientes' },
    { path: '/requisicoes', label: 'Requisições' },
    { path: '/provedores', label: 'Provedores' },
    { path: '/playground', label: 'Playground' },
  ];

  protected readonly grafanaUrl = `${location.protocol}//${location.hostname}:8330`;
}
