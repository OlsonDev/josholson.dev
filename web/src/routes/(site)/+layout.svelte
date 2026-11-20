<script lang="ts">
  import type { LayoutProps } from './$types';

  let { children }: LayoutProps = $props();
</script>

<main class="mx-auto max-w-4xl px-4 py-8">
  {@render children()}
</main>
