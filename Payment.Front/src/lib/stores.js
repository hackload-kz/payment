import { writable } from 'svelte/store';

export const booksStore = writable([]);

export async function loadBooks() {
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
```

## 📄 src/routes/+layout.svelte
```svelte
    < script >
    import '../app.css';
import { onMount } from 'svelte';
import { loadBooks } from '$lib/stores.js';

onMount(() => {
    loadBooks();
});
</script >

    <main>
        <slot />
    </main>