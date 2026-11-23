<script lang="ts">
  // The sections of one thing (a tag's Details and Used by) as links, one address each, so a refresh, back, or a shared link
  // lands on the same section. The current one is marked with `aria-current`. It looks like shadcn's tabs. Used by: tags.
  let { tabs, label }: { tabs: readonly { label: string; href: string; active: boolean }[]; label: string } = $props();
</script>

<nav
  aria-label={label}
  class="inline-flex h-9 w-fit items-center justify-center rounded-lg bg-muted p-[3px] text-muted-foreground"
>
  {#each tabs as tab (tab.href)}
    <a
      href={tab.href}
      aria-current={tab.active ? 'page' : undefined}
      class="inline-flex h-[calc(100%-1px)] items-center justify-center rounded-md border border-transparent px-3 py-1 text-sm font-medium whitespace-nowrap transition-all hover:text-foreground focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-none {tab.active
        ? 'bg-background text-foreground shadow-sm dark:border-input dark:bg-input/30'
        : ''}"
    >
      {tab.label}
    </a>
  {/each}
</nav>
