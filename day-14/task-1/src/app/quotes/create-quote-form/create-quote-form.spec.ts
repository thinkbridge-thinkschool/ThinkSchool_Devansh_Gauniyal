import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CreateQuoteForm } from './create-quote-form';

function setText(fixture: ComponentFixture<CreateQuoteForm>, value: string): void {
  const textarea = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
  textarea.value = value;
  textarea.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

function submit(fixture: ComponentFixture<CreateQuoteForm>): void {
  const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
  form.dispatchEvent(new Event('submit'));
  fixture.detectChanges();
}

describe('CreateQuoteForm', () => {
  let fixture: ComponentFixture<CreateQuoteForm>;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CreateQuoteForm],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(CreateQuoteForm);
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  // --- The four states named by the task -----------------------------------

  it('EMPTY: a freshly rendered form shows no error message and no aria-invalid', () => {
    const textarea = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
    const error = fixture.nativeElement.querySelector('#quote-text-error') as HTMLElement;

    expect(textarea.getAttribute('aria-invalid')).toBeNull();
    expect(textarea.getAttribute('aria-describedby')).toBeNull();
    expect(error.textContent?.trim()).toBe('');
  });

  it('INVALID: submitting empty marks the field invalid, renders the error, sets aria-invalid, and does not call the API', () => {
    submit(fixture);

    const textarea = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
    const error = fixture.nativeElement.querySelector('#quote-text-error') as HTMLElement;

    expect(textarea.getAttribute('aria-invalid')).toBe('true');
    expect(error.textContent?.trim()).toBe('Quote text is required.');
    httpMock.expectNone('/api/quotes');
  });

  it('SUBMITTING: the busy state is active in flight and the form cannot be double-submitted', () => {
    setText(fixture, 'A quote in flight');
    submit(fixture);

    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    const status = fixture.nativeElement.querySelector('.status') as HTMLElement;
    expect(button.disabled).toBe(true);
    expect(status.textContent).toContain('Submitting');

    // A second submit while the first request is still in flight must not
    // fire a second request -- expectOne below fails if more than one exists.
    submit(fixture);
    const req = httpMock.expectOne('/api/quotes');
    req.flush({ id: 9, ownerId: 'user-1', text: 'A quote in flight' });
  });

  it('SERVER-ERROR: a failing POST surfaces a visible, announced error and leaves the form usable again', () => {
    setText(fixture, 'A quote that will be rejected');
    submit(fixture);

    const req = httpMock.expectOne('/api/quotes');
    req.flush('nope', { status: 500, statusText: 'Internal Server Error' });
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    const status = fixture.nativeElement.querySelector('.status') as HTMLElement;
    const serverError = fixture.nativeElement.querySelector('.server-error') as HTMLElement;

    expect(button.disabled).toBe(false);
    expect(status.textContent?.trim()).toBe('');
    expect(serverError.getAttribute('role')).toBe('alert');
    expect(serverError.textContent?.trim().length).toBeGreaterThan(0);
  });

  it('emits quoteCreated with the real created quote on success, for the host page to add to the list', () => {
    let emitted: unknown;
    fixture.componentInstance.quoteCreated.subscribe((quote) => {
      emitted = quote;
    });

    setText(fixture, 'A quote the host list should receive');
    submit(fixture);

    const req = httpMock.expectOne('/api/quotes');
    req.flush({ id: 7, ownerId: 'user-1', text: 'A quote the host list should receive' });

    expect(emitted).toEqual({ id: 7, ownerId: 'user-1', text: 'A quote the host list should receive' });
  });

  // --- Contract tests --------------------------------------------------------

  it('CONTRACT: submits only the real "text" field, with the real field name and casing', () => {
    setText(fixture, 'Exact field name check');
    submit(fixture);

    const req = httpMock.expectOne('/api/quotes');
    expect(Object.keys(req.request.body)).toEqual(['text']);
    expect(req.request.body.text).toBe('Exact field name check');
    req.flush({ id: 1, ownerId: 'user-1', text: 'Exact field name check' });
  });

  it('CONTRACT: does not invent a maxLength the real DTO does not have', () => {
    // day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs:
    //   public sealed record CreateQuoteRequest(string Text);
    // carries no [MaxLength]/[StringLength] attribute, so an arbitrarily long
    // value must not be rejected client-side.
    setText(fixture, 'x'.repeat(5000));
    const textarea = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
    expect(textarea.getAttribute('aria-invalid')).toBeNull();
  });

  // --- Accessibility ----------------------------------------------------------

  it('A11Y: the input has a label whose for matches its id', () => {
    const label = fixture.nativeElement.querySelector('label') as HTMLLabelElement;
    const textarea = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
    expect(label.getAttribute('for')).toBe(textarea.id);
  });

  it('A11Y: aria-describedby resolves to an element present in the DOM in both the valid and invalid state', () => {
    const textareaValid = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
    const validRef = textareaValid.getAttribute('aria-describedby');
    if (validRef) {
      expect(fixture.nativeElement.querySelector(`#${validRef}`)).toBeTruthy();
    }

    submit(fixture);

    const textareaInvalid = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
    const invalidRef = textareaInvalid.getAttribute('aria-describedby');
    expect(invalidRef).toBe('quote-text-error');
    expect(fixture.nativeElement.querySelector(`#${invalidRef}`)).toBeTruthy();
  });

  it('A11Y: focus moves to the invalid field after a failed submit', () => {
    document.body.appendChild(fixture.nativeElement);
    submit(fixture);
    const textarea = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
    expect(document.activeElement).toBe(textarea);
    fixture.nativeElement.remove();
  });

  it('A11Y: no element has a positive tabindex', () => {
    const withTabIndex = fixture.nativeElement.querySelectorAll('[tabindex]');
    withTabIndex.forEach((el: Element) => {
      expect(Number(el.getAttribute('tabindex'))).toBeLessThanOrEqual(0);
    });
  });

  it('A11Y: the textarea and submit button are reachable in the tab order in the initial state', () => {
    const textarea = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(textarea.disabled).toBe(false);
    expect(button.disabled).toBe(false);
    expect(textarea.hasAttribute('tabindex')).toBe(false);
    expect(button.hasAttribute('tabindex')).toBe(false);
  });
});
