<script lang="ts">
  import * as Alert from '#lib/components/ui/alert/index.ts';
  import { Button } from '#lib/components/ui/button/index.ts';

  // What a save that did not go through says, above the form: that someone else saved the thing in between (with the
  // choice of loading their version or saving yours over it), or why it failed (`outcomeOfSaveError`). `noun` is what the
  // form edits ("tag"). Draws nothing when neither applies. Used by: tags.
  let {
    noun,
    conflict,
    failure,
    onreload,
    onoverwrite,
  }: {
    noun: string;
    conflict: boolean;
    failure: string | null;
    onreload: () => void;
    onoverwrite: () => void;
  } = $props();
</script>

{#if conflict}
  <Alert.Root variant="destructive" role="alert">
    <Alert.Title>Someone saved this {noun} after you opened it</Alert.Title>
    <Alert.Description>
      <p>
        Saving now would undo their changes. Load their version (and lose what you typed here), or save yours over it.
      </p>
      <div class="mt-3 flex gap-2">
        <Button type="button" variant="outline" size="sm" onclick={onreload}>Load their version</Button>
        <Button type="button" variant="destructive" size="sm" onclick={onoverwrite}>Save mine anyway</Button>
      </div>
    </Alert.Description>
  </Alert.Root>
{/if}
{#if failure !== null}
  <Alert.Root variant="destructive" role="alert"><Alert.Description>{failure}</Alert.Description></Alert.Root>
{/if}
