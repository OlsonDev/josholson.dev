<script lang="ts">
  import * as AlertDialog from '#lib/components/ui/alert-dialog/index.ts';

  import type { UnsavedGuard } from './unsaved-guard.svelte.ts';

  // What an `UnsavedGuard` asks when the person tries to leave a form with unsaved changes. Used by: tags.
  let { guard }: { guard: UnsavedGuard } = $props();

  // The safe answer has the focus when the question appears, so Enter or Space keeps the work, and discarding it takes a
  // Tab or a click.
  let keepEditing = $state<HTMLElement | null>(null);
</script>

<AlertDialog.Root
  open={guard.isOpen}
  onOpenChange={(open) => {
    if (!open) guard.stay();
  }}
>
  <AlertDialog.Content
    onOpenAutoFocus={(event) => {
      event.preventDefault();
      keepEditing?.focus();
    }}
  >
    <AlertDialog.Header>
      <AlertDialog.Title>Discard your changes?</AlertDialog.Title>
      <AlertDialog.Description
        >You have changes that are not saved. If you leave now, they are lost.</AlertDialog.Description
      >
    </AlertDialog.Header>
    <AlertDialog.Footer>
      <AlertDialog.Cancel bind:ref={keepEditing}>Keep editing</AlertDialog.Cancel>
      <!-- A solid red button, like Bootstrap's "danger". This preset's `destructive` variant is the soft one (a tint behind red text),
           and white on dark mode's lighter red does not reach 4.5:1, so dark mode uses the darker red. -->
      <AlertDialog.Action
        variant="destructive"
        class="bg-destructive text-white hover:bg-destructive/90 focus-visible:ring-destructive/40 dark:bg-red-600 dark:hover:bg-red-600/90"
        onclick={() => guard.leave()}
      >
        Discard changes
      </AlertDialog.Action>
    </AlertDialog.Footer>
  </AlertDialog.Content>
</AlertDialog.Root>
