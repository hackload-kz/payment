<script>
    import '../app.css';
    import { onMount } from 'svelte';
    import { loadBooks } from '$lib/stores';

    onMount(() => {
        loadBooks();
    });
</script>

<main>
    <slot />
</main>