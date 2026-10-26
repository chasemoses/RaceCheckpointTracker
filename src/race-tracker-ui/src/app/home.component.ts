import { Component, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { RaceStore } from './race-store';

@Component({
  selector: 'app-home',
  imports: [RouterLink, DatePipe, DecimalPipe],
  template: `
    <h1>Races</h1>
    <div class="cards">
      @for (r of store.races(); track r.id) {
        <a class="race-card" [routerLink]="['/races', r.id, 'results']">
          <strong>{{ r.name }}</strong>
          <span class="muted">{{ r.route }} · {{ r.totalDistanceKm | number: '1.0-1' }} km · {{ r.runnerCount }} runners</span>
          <span class="muted small">Started {{ r.startTime | date: 'h:mm a' }} · {{ r.checkpoints.length }} checkpoints</span>
        </a>
      } @empty {
        <p class="muted">No races yet.</p>
      }
    </div>
  `,
})
export class HomeComponent {
  readonly store = inject(RaceStore);
}
