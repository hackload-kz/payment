<!--
SPDX-FileCopyrightText: 2024-2025 Friedrich von Never <friedrich@fornever.me>

SPDX-License-Identifier: MIT
-->
<script lang="ts">
    import { onMount } from 'svelte';
    import { booksStore, type Book } from '$lib/stores';

    let books: Book[] = [];

    onMount(() => {
        const unsubscribe = booksStore.subscribe(value => {
            books = value;
        });

        return unsubscribe;
    });
</script>

<svelte:head>
    <title>Книжный магазин - Программирование</title>
    <meta name="description" content="Лучшие книги по программированию в Казахстане" />
</svelte:head>

<div class="container">
    <header>
        <h1>📚 Книжный магазин</h1>
        <p class="subtitle">Лучшие книги по программированию</p>
    </header>

    <div class="catalog-grid">
        {#each books as book}
            <a href="/book/{book.id}" class="book-card">
                <div class="book-image">{book.image}</div>
                <h3 class="book-title">{book.name}</h3>
                <p class="book-author">Автор: {book.authors}</p>
                <p class="book-description">{book.description}</p>
            </a>
        {/each}
    </div>
</div>