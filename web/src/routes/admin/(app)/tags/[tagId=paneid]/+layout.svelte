<script lang="ts">
  import { page } from '$app/state';

  import TagEditor from '#lib/tags/tag-editor.svelte';

  import type { LayoutProps } from './$types';

  let { data, children }: LayoutProps = $props();

  const tab = $derived(page.url.pathname.endsWith('/used-by') ? 'used-by' : 'details');
</script>

<!-- Another tag is another form, so it starts over with its own values. -->
{#key data.tag?.tagId ?? '+'}
  <TagEditor tag={data.tag} usage={data.usage} others={data.tags} {tab} />
{/key}
{@render children()}
