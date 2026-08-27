import { routes } from './app.routes';

describe('app.routes', () => {
  it('STRUCTURE: both quote-detail routes use loadComponent, not an eagerly-imported component', () => {
    const quotesRoute = routes.find((r) => r.path === 'quotes');
    const quotesIdRoute = routes.find((r) => r.path === 'quotes/:id');

    expect(quotesRoute?.loadComponent).toBeTruthy();
    expect(quotesRoute && 'component' in quotesRoute).toBe(false);
    expect(quotesIdRoute?.loadComponent).toBeTruthy();
    expect(quotesIdRoute && 'component' in quotesIdRoute).toBe(false);
  });
});
