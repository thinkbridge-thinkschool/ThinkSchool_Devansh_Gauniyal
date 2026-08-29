import { InjectionToken } from '@angular/core';

declare const BUILD_API_BASE_URL: string | undefined;

export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => '',
});

export function buildTimeApiBaseUrl(): string {
  const configured = typeof BUILD_API_BASE_URL === 'string' ? BUILD_API_BASE_URL : '';
  return configured.replace(/\/+$/, '');
}

export function apiUrl(baseUrl: string, path: string): string {
  return `${baseUrl.replace(/\/+$/, '')}/${path.replace(/^\/+/, '')}`;
}
