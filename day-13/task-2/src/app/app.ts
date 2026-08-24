import { Component } from '@angular/core';
import { QuoteBrowser } from './quotes/quote-browser/quote-browser';

@Component({
  selector: 'app-root',
  imports: [QuoteBrowser],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {}
