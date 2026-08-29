// Parity tests: both the reactive-forms version and the Signal Forms version
// must send the same real field names, with the same casing, for the same
// input, and must both refuse the same invalid input. This is what makes the
// comparison in comparison.md checkable rather than an assertion about code
// in two different places.
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CreateQuoteForm } from './create-quote-form/create-quote-form';
import { CreateQuoteFormSignals } from './create-quote-form-signals/create-quote-form-signals';

function setReactiveText(fixture: ComponentFixture<CreateQuoteForm>, value: string): void {
  const textarea = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
  textarea.value = value;
  textarea.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

function setReactiveAuthor(fixture: ComponentFixture<CreateQuoteForm>, value: string): void {
  const input = fixture.nativeElement.querySelector('#quote-author') as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

function submitReactive(fixture: ComponentFixture<CreateQuoteForm>): void {
  const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
  form.dispatchEvent(new Event('submit'));
  fixture.detectChanges();
}

function setSignalsText(fixture: ComponentFixture<CreateQuoteFormSignals>, value: string): void {
  const textarea = fixture.nativeElement.querySelector('#quote-text-signals') as HTMLTextAreaElement;
  textarea.value = value;
  textarea.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

function setSignalsAuthor(fixture: ComponentFixture<CreateQuoteFormSignals>, value: string): void {
  const input = fixture.nativeElement.querySelector('#quote-author-signals') as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

function submitSignals(fixture: ComponentFixture<CreateQuoteFormSignals>): void {
  const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
  form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  fixture.detectChanges();
}

describe('CreateQuoteForm vs CreateQuoteFormSignals parity', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('PARITY: both forms send the identical real field names and casing for the same input', () => {
    const reactiveFixture = TestBed.createComponent(CreateQuoteForm);
    reactiveFixture.detectChanges();
    setReactiveText(reactiveFixture, 'Security is a process.');
    setReactiveAuthor(reactiveFixture, 'Bruce Schneier');
    submitReactive(reactiveFixture);
    const reactiveReq = httpMock.expectOne('/api/quotes');
    const reactiveBody = reactiveReq.request.body;
    reactiveReq.flush({ id: 1, ownerId: 'user-1', text: 'Security is a process.', author: 'Bruce Schneier' });

    const signalsFixture = TestBed.createComponent(CreateQuoteFormSignals);
    signalsFixture.detectChanges();
    setSignalsText(signalsFixture, 'Security is a process.');
    setSignalsAuthor(signalsFixture, 'Bruce Schneier');
    submitSignals(signalsFixture);
    const signalsReq = httpMock.expectOne('/api/quotes');
    const signalsBody = signalsReq.request.body;
    signalsReq.flush({ id: 2, ownerId: 'user-1', text: 'Security is a process.', author: 'Bruce Schneier' });

    expect(Object.keys(reactiveBody).sort()).toEqual(Object.keys(signalsBody).sort());
    expect(reactiveBody).toEqual(signalsBody);
  });

  it('PARITY: both forms reject the same invalid input (both blank) without calling the API', () => {
    const reactiveFixture = TestBed.createComponent(CreateQuoteForm);
    reactiveFixture.detectChanges();
    submitReactive(reactiveFixture);
    httpMock.expectNone('/api/quotes');

    const signalsFixture = TestBed.createComponent(CreateQuoteFormSignals);
    signalsFixture.detectChanges();
    submitSignals(signalsFixture);
    httpMock.expectNone('/api/quotes');
  });

  it("CONTRACT: a field the real API doesn't have (e.g. 'title') appears in neither payload", () => {
    const reactiveFixture = TestBed.createComponent(CreateQuoteForm);
    reactiveFixture.detectChanges();
    setReactiveText(reactiveFixture, 'A quote');
    setReactiveAuthor(reactiveFixture, 'Someone');
    submitReactive(reactiveFixture);
    const reactiveReq = httpMock.expectOne('/api/quotes');
    expect(Object.keys(reactiveReq.request.body)).not.toContain('title');
    reactiveReq.flush({ id: 1, ownerId: 'user-1', text: 'A quote', author: 'Someone' });

    const signalsFixture = TestBed.createComponent(CreateQuoteFormSignals);
    signalsFixture.detectChanges();
    setSignalsText(signalsFixture, 'A quote');
    setSignalsAuthor(signalsFixture, 'Someone');
    submitSignals(signalsFixture);
    const signalsReq = httpMock.expectOne('/api/quotes');
    expect(Object.keys(signalsReq.request.body)).not.toContain('title');
    signalsReq.flush({ id: 2, ownerId: 'user-1', text: 'A quote', author: 'Someone' });
  });
});
