import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  ClientPatch,
  ClientView,
  CreatedClient,
  ModelRouteView,
  NewClient,
  ProviderStatus,
  RequestView,
  UsageOverview,
} from './models';

@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/admin-api';

  overview(hours: number): Promise<UsageOverview> {
    return this.get<UsageOverview>('/overview', new HttpParams().set('hours', hours));
  }

  clients(): Promise<ClientView[]> {
    return this.get<ClientView[]>('/clients');
  }

  createClient(request: NewClient): Promise<CreatedClient> {
    return firstValueFrom(this.http.post<CreatedClient>(`${this.base}/clients`, request));
  }

  updateClient(id: string, patch: ClientPatch): Promise<ClientView> {
    return firstValueFrom(this.http.patch<ClientView>(`${this.base}/clients/${id}`, patch));
  }

  rotateKey(id: string): Promise<CreatedClient> {
    return firstValueFrom(this.http.post<CreatedClient>(`${this.base}/clients/${id}/rotate-key`, {}));
  }

  requests(limit: number, clientId: string | null): Promise<RequestView[]> {
    let params = new HttpParams().set('limit', limit);
    if (clientId) {
      params = params.set('clientId', clientId);
    }
    return this.get<RequestView[]>('/requests', params);
  }

  providers(): Promise<ProviderStatus[]> {
    return this.get<ProviderStatus[]>('/providers');
  }

  models(): Promise<ModelRouteView[]> {
    return this.get<ModelRouteView[]>('/models');
  }

  private get<T>(path: string, params?: HttpParams): Promise<T> {
    return firstValueFrom(this.http.get<T>(`${this.base}${path}`, { params }));
  }
}

export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const message = error.error?.error?.message;
    if (typeof message === 'string') {
      return message;
    }
    return error.status === 0
      ? 'Não foi possível falar com o gateway.'
      : `O gateway respondeu com HTTP ${error.status}.`;
  }
  return error instanceof Error ? error.message : 'Erro inesperado.';
}
