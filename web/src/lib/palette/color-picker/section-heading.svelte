<script lang="ts">
  // A labeled rule between the parts of the popover. Used by: `ColorPicker`.
  let { label }: { label: string } = $props();
</script>

<div class="flex shrink-0 items-center gap-3">
  <h3 class="text-xs font-medium text-muted-foreground">{label}</h3>
  <span class="h-px flex-1 bg-border" aria-hidden="true"></span>
</div>
