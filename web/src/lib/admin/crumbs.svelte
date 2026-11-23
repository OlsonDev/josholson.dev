<script lang="ts">
  import { type Crumb, useBreadcrumbs } from './breadcrumbs.svelte.ts';

  // Puts a trail in the admin header's breadcrumbs for as long as it is on the page, and takes it out again after. It draws
  // nothing where it is written. The last item is where you are, and the ones before it are links back, so "one level up"
  // is the item next to it. Used by: tags.
  let { items }: { items: readonly Crumb[] } = $props();

  const trail = useBreadcrumbs();

  $effect(() => {
    trail.items = items;
    return () => {
      trail.items = [];
    };
  });
</script>
