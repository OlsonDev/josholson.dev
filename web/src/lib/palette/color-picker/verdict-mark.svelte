<script lang="ts">
  import CheckIcon from '@lucide/svelte/icons/check';
  import XIcon from '@lucide/svelte/icons/x';

  import type { Verdict } from '../colors.ts';

  // The mark for a contrast verdict: a check (green when it passes, yellow when it passes by 10% or less) or a red X. With
  // `srLabel` it also has text for a screen reader, for a place where the mark stands alone (a cell in the table); the
  // legend has visible text beside it, so it leaves that out. Used by: `ContrastTable`, `ContrastLegend`.
  let { verdict, srLabel = false }: { verdict: Verdict; srLabel?: boolean } = $props();

  const verdicts = {
    pass: { Icon: CheckIcon, color: 'text-green-600 dark:text-green-400', label: 'Met' },
    barely: { Icon: CheckIcon, color: 'text-yellow-600 dark:text-yellow-400', label: 'Barely met' },
    fail: { Icon: XIcon, color: 'text-red-600 dark:text-red-400', label: 'Not met' },
  } as const;

  const { Icon, color, label } = $derived(verdicts[verdict]);
</script>

<Icon class="size-4 {color}" aria-hidden="true" />
{#if srLabel}
  <span class="sr-only">{label}</span>
{/if}
