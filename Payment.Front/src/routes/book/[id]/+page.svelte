<!--
SPDX-FileCopyrightText: 2024-2025 Friedrich von Never <friedrich@fornever.me>

SPDX-License-Identifier: MIT
-->
<script lang="ts">
  import { page } from '$app/stores';
  import { booksStore, type Book } from '$lib/stores';
  import { onMount } from 'svelte';

  let book: Book | undefined = undefined;
  let loading = true;

  onMount(() => {
    const unsubscribe = booksStore.subscribe(books => {
      if (books.length > 0) {
        const bookId = parseInt($page.params.id);
        book = books.find(b => b.id === bookId);
        loading = false;
      }
    });

    return unsubscribe;
  });
</script>

<svelte:head>
  {#if book}
  <title>{book.name} - Книжный магазин</title>
  <meta name="description" content="{book.description}" />
  {/if}
</svelte:head>

<div class="container">
  {#if loading}
  <div class="book-detail-content">
    <p>Загрузка...</p>
  </div>
  {:else if book}
  <div class="book-detail-content">
    <a href="/" class="back-btn">← Назад к каталогу</a>

    <div class="detail-image">{book.image}</div>
    <h2 class="detail-title">{book.name}</h2>
    <p class="detail-author">Автор: {book.authors}</p>

    {#if book.isbn}
    <div class="detail-isbn">ISBN: {book.isbn}</div>
    {/if}

    <p class="detail-description">{book.description}</p>
    <a href="{book.link}" target="_blank" rel="noopener noreferrer" class="external-link">
      Перейти к книге
    </a>
    <form method="POST" action="?/buy">
      <input type="hidden" name="bookId" value={book.id} />
      <button type="submit" class="buy-btn external-link" aria-label="Купить">Купить</button>
    </form>
  </div>
  {:else}
  <div class="book-detail-content">
    <a href="/" class="back-btn">← Назад к каталогу</a>
    <h2>Книга не найдена</h2>
    <p>Извините, запрашиваемая книга не найдена.</p>
  </div>
  {/if}
</div>