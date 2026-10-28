import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App', () => {
  it('renders the navigation for every admin section', async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    }).compileComponents();

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const links = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('.nav__link'),
    ).map((link) => link.textContent?.trim());

    expect(links).toEqual(['Visão geral', 'Clientes', 'Requisições', 'Provedores', 'Playground']);
  });
});
