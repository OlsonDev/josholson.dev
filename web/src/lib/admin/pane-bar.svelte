<script lang="ts">
  import type { Snippet } from 'svelte';

  import { Button } from '#lib/components/ui/button/index.ts';

  import { usePane } from './pane.svelte.ts';

  // The Save and Cancel bar of a pane (Save, a second way to submit if there is one, Cancel, then a divider and the message,
  // and at the far right the dangerous action), anchored to the bottom of the list page so a short window or a tall form never
  // hides it. It draws nothing where it is written: it hands its markup to the list page (`ListPane`), which shows it
  // and animates it from below. The submit button belongs to the form with the ID `formId`, since the bar is outside it.
  // `summary` is the count of validation errors, `isDirty` says there are unsaved changes, and `extra` is a second way to submit (such
  // as "Save and add another") beside Save, and `danger` is a button such as Delete, kept at the far right. Used by: tags.
  let {
    formId,
    saveLabel,
    summary = null,
    isDirty = false,
    isBusy = false,
    onCancel,
    extra,
    danger,
  }: {
    formId: string;
    saveLabel: string;
    summary?: string | null;
    isDirty?: boolean;
    isBusy?: boolean;
    onCancel: () => void;
    extra?: Snippet;
    danger?: Snippet;
  } = $props();

  const pane = usePane();

  $effect(() => {
    pane.bar = bar;
    return () => {
      pane.bar = null;
    };
  });
</script>

{#snippet bar()}
  <div class="flex flex-wrap items-center gap-3 border-t bg-background px-4 py-3 md:px-6">
    <Button type="submit" form={formId} disabled={isBusy}>{isBusy ? 'Saving…' : saveLabel}</Button>
    {#if extra}{@render extra()}{/if}
    <Button type="button" variant="outline" onclick={onCancel}>Cancel</Button>
    <!-- The message, after a divider, when there is one. The region is always here, so a screen reader hears it appear. -->
    {#if summary !== null || isDirty}
      <span class="h-5 w-px bg-border" aria-hidden="true"></span>
    {/if}
    <p class="min-w-0 flex-1 text-sm" role="status">
      {#if summary !== null}
        <span class="font-medium text-destructive">{summary}</span>
      {:else if isDirty}
        <span class="text-muted-foreground">You have unsaved changes.</span>
      {/if}
    </p>
    {#if danger}
      {@render danger()}
    {/if}
  </div>
{/snippet}
