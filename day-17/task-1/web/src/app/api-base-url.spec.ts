import { TestBed } from '@angular/core/testing';
import { API_BASE_URL, apiUrl, buildTimeApiBaseUrl } from './api-base-url';

describe('API base URL configuration', () => {
  it('defaults to the carried relative URL behavior', () => {
    expect(TestBed.inject(API_BASE_URL)).toBe('');
    expect(buildTimeApiBaseUrl()).toBe('');
  });

  it('joins a relative endpoint without introducing a double slash', () => {
    expect(apiUrl('', '/api/quotes')).toBe('/api/quotes');
  });

  it('joins an absolute Function origin and endpoint', () => {
    expect(apiUrl('https://function.example.invalid/', '/api/quotes')).toBe(
      'https://function.example.invalid/api/quotes',
    );
  });
});
