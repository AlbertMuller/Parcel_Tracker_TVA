import { Routes } from '@angular/router';
import { OrderDetail } from './orders/order-detail';
import { OrderForm } from './orders/order-form';
import { OrderList } from './orders/order-list';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'orders' },
  { path: 'orders', component: OrderList, title: 'Orders' },
  { path: 'orders/new', component: OrderForm, title: 'New order' },
  { path: 'orders/:id', component: OrderDetail, title: 'Order details' },
  { path: '**', redirectTo: 'orders' },
];
