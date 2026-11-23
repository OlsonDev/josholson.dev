<script lang="ts">
  import type { Snippet } from 'svelte';

  import { Label } from '#lib/components/ui/label/index.ts';

  import type { FieldControl } from './field.ts';

  // One labeled input of an edit form, with its help text and what is wrong with it. `name` is what the form's errors are
  // keyed by (`FormState`), and it is also how `focusFirstInvalid` finds the field. The input goes in the snippet, which
  // is told the `id` to use and what to put in `aria-describedby`. Used by: tags.
  let {
    name,
    label,
    description,
    errors = [],
    children,
  }: {
    name: string;
    label: string;
    description?: string;
    errors?: readonly string[];
    children: Snippet<[FieldControl]>;
  } = $props();

  const uid = $props.id();
  const id = $derived(`${uid}-control`);
  const descriptionId = $derived(`${uid}-description`);
  const errorId = $derived(`${uid}-error`);
  const invalid = $derived(errors.length > 0);
  const describedBy = $derived(
    [description === undefined ? null : descriptionId, invalid ? errorId : null].filter(Boolean).join(' ') || undefined,
  );
</script>

<div class="grid gap-2" data-field={name}>
  <Label for={id}>{label}</Label>
  <!-- The control has a block of its own, so an element an extension puts beside it (a password manager's icon) is in this
       block and not a row of the grid, where the gap around it would push the help text down. -->
  <div>
    {@render children({ id, describedBy, invalid })}
  </div>
  {#if description !== undefined}
    <p id={descriptionId} class="text-sm text-muted-foreground">{description}</p>
  {/if}
  {#if invalid}
    <p id={errorId} class="text-sm text-destructive">{errors.join(' ')}</p>
  {/if}
</div>
