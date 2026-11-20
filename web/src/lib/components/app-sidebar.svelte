<script lang="ts">
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import LogOutIcon from '@lucide/svelte/icons/log-out';
  import type { ComponentProps } from 'svelte';

  import { adminPages, isCurrent, passkeysPage } from '#lib/admin/navigation.ts';
  import { nullOnApiError, signIn } from '#lib/api/index.ts';
  import type { CurrentUserResponse } from '#lib/api/schema.ts';
  import * as Sidebar from '#lib/components/ui/sidebar/index.ts';

  let {
    user,
    ref = $bindable(null),
    ...restProps
  }: ComponentProps<typeof Sidebar.Root> & { user: CurrentUserResponse } = $props();

  async function signOut(): Promise<void> {
    await signIn.signOut(nullOnApiError);
    await goto('/admin/sign-in', { invalidateAll: true });
  }
</script>

<Sidebar.Root bind:ref {...restProps}>
  <Sidebar.Header>
    <Sidebar.Menu>
      <Sidebar.MenuItem>
        <Sidebar.MenuButton size="lg">
          {#snippet child({ props })}
            <a href="/admin" {...props}>
              <div class="flex flex-col gap-0.5 leading-none">
                <span class="font-medium">Josh Olson</span>
                <span class="text-xs text-muted-foreground">Admin</span>
              </div>
            </a>
          {/snippet}
        </Sidebar.MenuButton>
      </Sidebar.MenuItem>
    </Sidebar.Menu>
  </Sidebar.Header>
  <Sidebar.Content>
    <Sidebar.Group>
      <Sidebar.GroupContent>
        <Sidebar.Menu>
          {#each adminPages as item (item.href)}
            <Sidebar.MenuItem>
              <Sidebar.MenuButton isActive={isCurrent(item, page.url.pathname)}>
                {#snippet child({ props })}
                  <a href={item.href} {...props}>
                    <item.icon />
                    <span>{item.title}</span>
                  </a>
                {/snippet}
              </Sidebar.MenuButton>
            </Sidebar.MenuItem>
          {/each}
        </Sidebar.Menu>
      </Sidebar.GroupContent>
    </Sidebar.Group>
  </Sidebar.Content>
  <Sidebar.Footer>
    <p class="px-2 text-xs text-muted-foreground">
      Signed in as <span class="font-medium text-foreground">{user.displayName}</span>
    </p>
    <Sidebar.Menu>
      <Sidebar.MenuItem>
        <Sidebar.MenuButton isActive={isCurrent(passkeysPage, page.url.pathname)}>
          {#snippet child({ props })}
            <a href={passkeysPage.href} {...props}>
              <passkeysPage.icon />
              <span>{passkeysPage.title}</span>
            </a>
          {/snippet}
        </Sidebar.MenuButton>
      </Sidebar.MenuItem>
      <Sidebar.MenuItem>
        <Sidebar.MenuButton onclick={signOut}>
          <LogOutIcon />
          <span>Sign out</span>
        </Sidebar.MenuButton>
      </Sidebar.MenuItem>
    </Sidebar.Menu>
  </Sidebar.Footer>
  <Sidebar.Rail />
</Sidebar.Root>
