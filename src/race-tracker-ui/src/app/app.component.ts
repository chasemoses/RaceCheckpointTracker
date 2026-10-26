import { Component, computed, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { RaceStore } from './race-store';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, DatePipe, DecimalPipe],
  template: `
    <header class="topbar">
      <a class="brand" routerLink="/">Race Checkpoint Tracker</a>
      <nav>
        @for (r of store.races(); track r.id) {
          <a [routerLink]="['/races', r.id, 'results']" [class.active]="r.id === raceId()">{{ r.name }}</a>
        }
      </nav>
    </header>

    <main class="page">
      @if (race(); as r) {
        <div class="race-head">
          <div>
            <h1>{{ r.name }}</h1>
            <p class="muted">
              {{ r.route === 'Loop' ? r.laps + ' laps · ' : '' }}{{ r.totalDistanceKm | number: '1.0-1' }} km ·
              {{ r.runnerCount }} runners · started {{ r.startTime | date: 'h:mm a' }}
            </p>
          </div>
          <nav class="tabs">
            <a [routerLink]="['/races', r.id, 'results']" routerLinkActive="active">Leaderboard</a>
            <a [routerLink]="['/races', r.id, 'entry']" routerLinkActive="active">Checkpoint entry</a>
          </nav>
        </div>
      }
      <router-outlet />
    </main>
  `,
})
export class AppComponent {
  readonly store = inject(RaceStore);
  private router = inject(Router);

  readonly raceId = toSignal(this.router.events.pipe(
    filter(e => e instanceof NavigationEnd),
    map(e => Number(/^\/races\/(\d+)/.exec((e as NavigationEnd).urlAfterRedirects)?.[1]) || null),
  ));
  readonly race = computed(() => this.store.races().find(r => r.id === this.raceId()));
}
