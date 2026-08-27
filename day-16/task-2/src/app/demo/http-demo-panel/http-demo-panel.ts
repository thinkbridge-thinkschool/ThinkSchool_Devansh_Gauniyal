/**
 * DEMO ONLY -- not part of the graded interceptor work, not covered by any automated
 * test, and not required by the exercise. Exists purely so the real interceptor chain
 * (auth header, retry, error mapping -- see ../../http/) can be driven live, on demand,
 * against the real QuotesApi, without needing to throttle DevTools or count network
 * requests by eye.
 *
 * "GET /api/quotes" hits the real success route. "GET /api/quotes/999" hits a route
 * this API genuinely does not expose for GET (see quotes-api-contract.spec.ts) --
 * real capture: 405 Method Not Allowed, empty body -- so it is a real, live 4xx, not a
 * simulated one, and it proves the same "not retried" behaviour a live 500 would.
 */
import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { AppHttpError } from '../../http/app-http-error';
import { Quote } from '../../quotes/quote';
import { RequestCounterService } from '../request-counter.service';

interface DemoSuccess {
  readonly kind: 'success';
  readonly quotes: Quote[];
}

interface DemoError {
  readonly kind: 'error';
  readonly message: string;
  readonly status: number;
  readonly retryable: boolean;
}

type DemoResult = DemoSuccess | DemoError | null;

@Component({
  selector: 'app-http-demo-panel',
  imports: [],
  templateUrl: './http-demo-panel.html',
  styleUrl: './http-demo-panel.css',
})
export class HttpDemoPanel {
  private readonly http = inject(HttpClient);
  private readonly requestCounter = inject(RequestCounterService);

  readonly result = signal<DemoResult>(null);
  readonly loading = signal(false);
  readonly requestCount = this.requestCounter.count;
  readonly lastUrl = signal<string | null>(null);

  runSuccess(): void {
    this.run('/api/quotes');
  }

  run4xx(): void {
    this.run('/api/quotes/999');
  }

  reset(): void {
    this.result.set(null);
    this.lastUrl.set(null);
    this.requestCounter.reset();
  }

  private run(url: string): void {
    this.loading.set(true);
    this.result.set(null);
    this.lastUrl.set(url);
    this.requestCounter.reset();

    this.http.get<Quote[]>(url).subscribe({
      next: (quotes) => {
        this.result.set({ kind: 'success', quotes });
        this.loading.set(false);
      },
      error: (err: unknown) => {
        if (err instanceof AppHttpError) {
          // Mirrors retry.interceptor.ts's isTransientFailure predicate, purely for
          // display here -- this panel does not affect and is not affected by the
          // real retry logic, which already ran (or didn't) before this ever sees it.
          const retryable = err.status === 0 || err.status >= 500;
          this.result.set({ kind: 'error', message: err.friendlyMessage, status: err.status, retryable });
        } else {
          this.result.set({ kind: 'error', message: 'Unknown error', status: 0, retryable: false });
        }
        this.loading.set(false);
      },
    });
  }
}
