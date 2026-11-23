<script lang="ts">
  import { ApiError } from '#lib/api/index.ts';
  import type { DeletionImpact, DeletionImpactItem } from '#lib/api/schema.ts';
  import * as AlertDialog from '#lib/components/ui/alert-dialog/index.ts';
  import * as Alert from '#lib/components/ui/alert/index.ts';
  import { Button } from '#lib/components/ui/button/index.ts';
  import { Input } from '#lib/components/ui/input/index.ts';
  import { Label } from '#lib/components/ui/label/index.ts';

  import { describeImpactItem, impactKinds } from './impact-kinds.ts';
  import { impactFromError, isBlocked } from './impact.ts';

  // The confirmation before deleting anything in the admin. It asks the API what the delete would affect (`loadImpact`),
  // then shows one of four things:
  //   - a short wait while it asks;
  //   - blockers: the thing is used in a way that forbids deleting it, so there is nothing to confirm;
  //   - warnings: what the delete also removes or changes, and then the confirmation, which is typing `confirmPhrase`
  //     (leave it out for something trivial, and the button alone confirms);
  //   - nothing to say, and the confirmation alone.
  // The API decides what is a blocker and what is a warning (`DeletionImpact`), and `impactKinds` is how each kind of thing
  // is named here. Deleting asks the API again, so a thing that became blocked since the dialog opened is shown as blocked.
  // Used by: tags. Add the page you use it on here.
  let {
    open = $bindable(),
    noun,
    name,
    confirmPhrase = null,
    loadImpact,
    remove,
    onDeleted,
  }: {
    open: boolean;
    /** What it is, lowercase: "tag". */
    noun: string;
    /** What this one is called. */
    name: string;
    /** What the person types to confirm, such as `delete-foo`, or null to need no typing. */
    confirmPhrase?: string | null;
    loadImpact: () => Promise<DeletionImpact>;
    remove: () => Promise<void>;
    onDeleted: () => void;
  } = $props();

  let impact = $state<DeletionImpact | null>(null);
  let loading = $state(false);
  let problem = $state<string | null>(null);
  let typed = $state('');
  let deleting = $state(false);

  const blocked = $derived(impact !== null && isBlocked(impact));
  const confirmed = $derived(confirmPhrase === null || typed === confirmPhrase);

  async function load(): Promise<void> {
    loading = true;
    problem = null;
    impact = null;
    typed = '';
    try {
      impact = await loadImpact();
    } catch (error) {
      problem =
        error instanceof ApiError && error.status === 404
          ? `This ${noun} no longer exists.`
          : `Could not check what uses this ${noun}.`;
    } finally {
      loading = false;
    }
  }

  async function confirm(): Promise<void> {
    if (!confirmed || deleting) return;
    deleting = true;
    problem = null;
    try {
      await remove();
      open = false;
      onDeleted();
    } catch (error) {
      const refused = impactFromError(error);
      if (refused !== null) {
        impact = refused;
      } else if (error instanceof ApiError && error.status === 404) {
        // Already gone, which is what the person wanted.
        open = false;
        onDeleted();
      } else {
        problem = `The ${noun} could not be deleted.`;
      }
    } finally {
      deleting = false;
    }
  }

  $effect(() => {
    if (open) void load();
  });
</script>

{#snippet items(list: DeletionImpactItem[])}
  <ul class="grid gap-1.5 text-sm">
    {#each list as item (item.kind)}
      {@const words = impactKinds[item.kind]}
      <li class="flex items-center gap-2">
        <words.icon class="size-4 text-muted-foreground" aria-hidden="true" />
        <a class="underline-offset-4 hover:underline" href={words.href}>{describeImpactItem(item)}</a>
      </li>
    {/each}
  </ul>
{/snippet}

<AlertDialog.Root bind:open>
  <AlertDialog.Content>
    <AlertDialog.Header>
      <AlertDialog.Title>Delete the {noun} “{name}”?</AlertDialog.Title>
      <AlertDialog.Description>
        {#if blocked}
          This {noun} cannot be deleted.
        {:else}
          This cannot be undone.
        {/if}
      </AlertDialog.Description>
    </AlertDialog.Header>

    <div class="grid gap-4" aria-live="polite">
      {#if loading}
        <p class="text-sm text-muted-foreground">Checking what uses this {noun}…</p>
      {:else if impact !== null}
        {#if blocked}
          <Alert.Root variant="destructive">
            <Alert.Title>It is in use</Alert.Title>
            <Alert.Description>
              <p>Deleting the {noun} is blocked by:</p>
              {@render items(impact.blockers)}
            </Alert.Description>
          </Alert.Root>
        {:else if impact.warnings.length > 0}
          <Alert.Root>
            <Alert.Title>It will also be removed from</Alert.Title>
            <Alert.Description>{@render items(impact.warnings)}</Alert.Description>
          </Alert.Root>
        {/if}
        {#if !blocked && confirmPhrase !== null}
          <div class="grid gap-2">
            <Label for="confirm-delete-phrase">
              Type <code class="rounded bg-muted px-1 py-0.5 font-mono text-sm">{confirmPhrase}</code> to confirm
            </Label>
            <Input id="confirm-delete-phrase" bind:value={typed} autocomplete="off" spellcheck="false" />
          </div>
        {/if}
      {/if}
      {#if problem !== null}
        <Alert.Root variant="destructive"><Alert.Description>{problem}</Alert.Description></Alert.Root>
      {/if}
    </div>

    <AlertDialog.Footer>
      <AlertDialog.Cancel>{blocked ? 'Close' : 'Cancel'}</AlertDialog.Cancel>
      {#if !blocked}
        <Button variant="destructive" disabled={loading || impact === null || !confirmed || deleting} onclick={confirm}>
          {deleting ? 'Deleting…' : `Delete ${noun}`}
        </Button>
      {/if}
    </AlertDialog.Footer>
  </AlertDialog.Content>
</AlertDialog.Root>
