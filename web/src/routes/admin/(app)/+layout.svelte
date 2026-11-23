<script lang="ts">
  import { page } from '$app/state';

  import { provideBreadcrumbs } from '#lib/admin/breadcrumbs.svelte.ts';
  import { pageAt } from '#lib/admin/navigation.ts';
  import AppSidebar from '#lib/components/app-sidebar.svelte';
  import * as Breadcrumb from '#lib/components/ui/breadcrumb/index.ts';
  import { Separator } from '#lib/components/ui/separator/index.ts';
  import * as Sidebar from '#lib/components/ui/sidebar/index.ts';

  import type { LayoutProps } from './$types';

  let { data, children }: LayoutProps = $props();

  const current = $derived(pageAt(page.url.pathname));
  // A page inside a page gives its own trail after "Admin" (see `Crumbs`); otherwise it is the page's title.
  const trail = provideBreadcrumbs();
  const crumbs = $derived(
    trail.items.length > 0
      ? trail.items
      : current !== undefined && current.href !== '/admin'
        ? [{ label: current.title }]
        : [],
  );
</script>

<Sidebar.Provider>
  <AppSidebar user={data.user} />
  <Sidebar.Inset>
    <header class="flex h-14 shrink-0 items-center gap-2 border-b px-4">
      <Sidebar.Trigger class="-ml-1" />
      <Separator orientation="vertical" class="mr-2" />
      <Breadcrumb.Root>
        <Breadcrumb.List>
          <Breadcrumb.Item>
            <Breadcrumb.Link href="/admin">Admin</Breadcrumb.Link>
          </Breadcrumb.Item>
          {#each crumbs as crumb, index (index)}
            <Breadcrumb.Separator />
            <Breadcrumb.Item>
              {#if crumb.href !== undefined && index < crumbs.length - 1}
                <Breadcrumb.Link href={crumb.href}>{crumb.label}</Breadcrumb.Link>
              {:else}
                <Breadcrumb.Page>{crumb.label}</Breadcrumb.Page>
              {/if}
            </Breadcrumb.Item>
          {/each}
        </Breadcrumb.List>
      </Breadcrumb.Root>
    </header>
    <div class="flex flex-1 flex-col gap-6 p-4 md:p-6">
      {@render children()}
    </div>
  </Sidebar.Inset>
</Sidebar.Provider>
