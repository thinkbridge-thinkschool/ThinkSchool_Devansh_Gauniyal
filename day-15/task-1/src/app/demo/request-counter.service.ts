import { Injectable, signal } from '@angular/core';

// DEMO ONLY -- not part of the graded interceptor work, exists solely so the demo
// panel (see http-demo-panel/) can show how many real requests actually left the
// browser for a given click, including any retries, without needing DevTools open.
@Injectable({ providedIn: 'root' })
export class RequestCounterService {
  readonly count = signal(0);

  increment(): void {
    this.count.update((n) => n + 1);
  }

  reset(): void {
    this.count.set(0);
  }
}
