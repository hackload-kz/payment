<script>
  import { page } from '$app/stores';
  import { booksStore } from '$lib/stores.js';
  import { onMount } from 'svelte';

  let book = null;
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
  </div>
  {:else}
  <div class="book-detail-content">
    <a href="/" class="back-btn">← Назад к каталогу</a>
    <h2>Книга не найдена</h2>
    <p>Извините, запрашиваемая книга не найдена.</p>
  </div>
  {/if}
</div>