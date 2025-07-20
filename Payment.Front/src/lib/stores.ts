/*
SPDX-FileCopyrightText: 2024-2025 Friedrich von Never <friedrich@fornever.me>

SPDX-License-Identifier: MIT
*/
import { writable } from 'svelte/store';

export const booksStore = writable<Book[]>([]);

export interface Book {
    id: number;
    authors: string;
    name: string;
    description: string;
    image: string;
    isbn: string;
    link: string;
}

export async function loadBooks() : Promise<Book[]> {
    try {
        const response = await fetch('/books.json');
        const books = await response.json();
        booksStore.set(books);
        return books;
    } catch (error) {
        console.error('Ошибка загрузки книг:', error);
        return [];
    }
}