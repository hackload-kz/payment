/*
SPDX-FileCopyrightText: 2024-2025 Friedrich von Never <friedrich@fornever.me>

SPDX-License-Identifier: MIT
*/
import { fail, redirect } from '@sveltejs/kit';
//import * as db from '$lib/server/db';
import type { Actions } from './$types';

export const actions = {
  buy: async ({ request }) => {
    const formData = await request.formData();
    const bookId = formData.get('bookId');
    return redirect(303, 'https://hackload.kz/');
  }
} satisfies Actions;