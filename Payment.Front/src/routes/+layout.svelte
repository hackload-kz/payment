<!--
SPDX-FileCopyrightText: 2024-2025 Friedrich von Never <friedrich@fornever.me>

SPDX-License-Identifier: MIT
-->
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