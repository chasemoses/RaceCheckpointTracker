import { Injectable, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RaceApi } from './race-api';

/** The race list, loaded once and shared by every screen. */
@Injectable({ providedIn: 'root' })
export class RaceStore {
  readonly races = toSignal(inject(RaceApi).races(), { initialValue: [] });
}
