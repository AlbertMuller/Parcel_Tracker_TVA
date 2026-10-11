import { Component, input } from '@angular/core';
import { OrderStatus } from '../orders/order.models';

@Component({
  selector: 'app-status-badge',
  template: `<span [class]="'badge badge-' + status().toLowerCase()">{{ status() }}</span>`,
})
export class StatusBadge {
  readonly status = input.required<OrderStatus>();
}
