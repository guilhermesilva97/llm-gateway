import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'visao-geral' },
  {
    path: 'visao-geral',
    title: 'Visão geral · LLM Gateway',
    loadComponent: () => import('./pages/overview.page').then((m) => m.OverviewPage),
  },
  {
    path: 'clientes',
    title: 'Clientes · LLM Gateway',
    loadComponent: () => import('./pages/clients.page').then((m) => m.ClientsPage),
  },
  {
    path: 'requisicoes',
    title: 'Requisições · LLM Gateway',
    loadComponent: () => import('./pages/requests.page').then((m) => m.RequestsPage),
  },
  {
    path: 'provedores',
    title: 'Provedores · LLM Gateway',
    loadComponent: () => import('./pages/providers.page').then((m) => m.ProvidersPage),
  },
  {
    path: 'playground',
    title: 'Playground · LLM Gateway',
    loadComponent: () => import('./pages/playground.page').then((m) => m.PlaygroundPage),
  },
  { path: '**', redirectTo: 'visao-geral' },
];
