<script lang="ts">
  import { goto, invalidate } from '$app/navigation';
  import { page } from '$app/state';
  import { tick } from 'svelte';

  import ConfirmDelete from '#lib/admin/confirm-delete.svelte';
  import Crumbs from '#lib/admin/crumbs.svelte';
  import Field from '#lib/admin/field.svelte';
  import { type FieldControl, noAutofill } from '#lib/admin/field.ts';
  import { FormState } from '#lib/admin/form.svelte.ts';
  import { describeImpactItem, impactKinds } from '#lib/admin/impact-kinds.ts';
  import { normalizeSearch } from '#lib/admin/list-state.ts';
  import PaneBar from '#lib/admin/pane-bar.svelte';
  import { nextSlug } from '#lib/admin/slug.ts';
  import TabLinks from '#lib/admin/tab-links.svelte';
  import UnsavedChangesDialog from '#lib/admin/unsaved-changes-dialog.svelte';
  import { UnsavedGuard } from '#lib/admin/unsaved-guard.svelte.ts';
  import { ApiError, tag as tagApi } from '#lib/api/index.ts';
  import type { DeletionImpact, TagResponse } from '#lib/api/schema.ts';
  import { describeFailure } from '#lib/auth/failure.ts';
  import * as Alert from '#lib/components/ui/alert/index.ts';
  import { Button } from '#lib/components/ui/button/index.ts';
  import { Input } from '#lib/components/ui/input/index.ts';
  import { Textarea } from '#lib/components/ui/textarea/index.ts';
  import ColorPicker from '#lib/palette/color-picker/color-picker.svelte';
  import type { PreviewColors } from '#lib/palette/color-picker/color-picker.ts';

  import { listHref, paneHref } from './routes.ts';
  import * as tagForm from './tag-form.ts';

  // The edit form of one tag, or of a new one when `tag` is null, shown beside the list (`ListPane`). It is the first user
  // of the admin's list-and-edit parts, so each is one a later page takes as it is: `FormState` (values, dirty, errors),
  // `Field`, `PaneBar`, `UnsavedGuard`, and `ConfirmDelete`.
  //
  // The form is shown on the Details section and a list of what uses the tag on the Used by section, each with an address
  // of its own (`/admin/tags/5` and `/admin/tags/5/used-by`), so a refresh stays on the section. Both are always in the
  // page, only one is visible, so the form and what was typed survive a switch and Save works from either section.
  let {
    tag,
    usage,
    others,
    tab,
  }: {
    tag: TagResponse | null;
    usage: DeletionImpact | null;
    others: readonly TagResponse[];
    tab: 'details' | 'used-by';
  } = $props();

  // Whatever the form is opened for stays for as long as it is open: the page makes a new one for another tag.
  // svelte-ignore state_referenced_locally
  const isNew = tag === null;
  // svelte-ignore state_referenced_locally
  const tagId = tag?.tagId ?? null;

  // The list's page, which every address here carries so closing returns to it.
  const search = $derived(normalizeSearch(page.url.searchParams));
  // svelte-ignore state_referenced_locally
  let version = $state<number | null>(tag?.version ?? null);
  let busy = $state(false);
  let failure = $state<string | null>(null);
  let conflict = $state(false);
  let confirmingDelete = $state(false);
  // TEMP: an optional color, only to try the picker with a "No color" option. It is not part of the form and is never saved.
  let temporaryColor = $state<string | null>(null);
  let root = $state<HTMLElement | null>(null);

  // svelte-ignore state_referenced_locally
  const form = new FormState<tagForm.Values>(tag === null ? tagForm.blank() : tagForm.fromResponse(tag), {
    validate: (values) => tagForm.validate(values, tagId, others),
    // A field with an error is on the Details section, so that is where to be before focusing it.
    reveal: async () => {
      if (tab !== 'details') await goto(paneHref(tagId ?? '+', search));
    },
  });
  // Moving to the other section of this tag is not leaving the form.
  const here = `/admin/tags/${tagId?.toString() ?? '+'}`;
  const guard = new UnsavedGuard(
    () => form.isDirty,
    (url) => url.pathname === here || url.pathname === `${here}/used-by`,
  );

  const slugChanged = $derived(
    !isNew && tag !== null && form.values.slug.trim() !== tag.slug && (form.errors['slug'] ?? []).length === 0,
  );

  function changeName(event: Event & { currentTarget: HTMLInputElement }): void {
    const next = event.currentTarget.value;
    // A new tag's slug follows its name until the slug is edited. An existing tag's slug is a URL, so it stays.
    if (isNew) {
      form.values.slug = nextSlug(form.values.name, next, form.values.slug);
      form.clear('slug');
    }
    form.values.name = next;
    form.clear('name');
  }

  async function addAlias(): Promise<void> {
    form.values.aliases.push('');
    form.clear('aliases');
    await tick();
    const inputs = root?.querySelectorAll<HTMLInputElement>('input[data-field^="aliases["]');
    inputs?.item(inputs.length - 1).focus();
  }

  function removeAlias(index: number): void {
    form.values.aliases.splice(index, 1);
    form.clear('aliases');
  }

  async function save(event: SubmitEvent): Promise<void> {
    event.preventDefault();
    if (busy) return;
    failure = null;
    conflict = false;
    if (!form.check()) {
      if (root !== null) await form.focusFirstInvalid(root);
      return;
    }
    const again = isNew && event.submitter instanceof HTMLElement && event.submitter.dataset['again'] === 'true';
    await submit({ again, overwrite: false });
  }

  async function submit({ again, overwrite }: { again: boolean; overwrite: boolean }): Promise<void> {
    busy = true;
    try {
      const request = tagForm.toRequest(form.values, overwrite ? null : version);
      if (tagId === null) {
        await tagApi.create(request);
      } else {
        await tagApi.update({ tagId }, request);
      }
      await invalidate('app:tags');
      if (again) {
        form.reset(tagForm.blank());
        await tick();
        root?.querySelector<HTMLInputElement>('input[data-field="name"]')?.focus();
      } else {
        form.reset();
        await goto(listHref(search));
      }
    } catch (error) {
      if (error instanceof ApiError && error.is(409)) {
        conflict = true;
      } else if (error instanceof ApiError && error.is(422)) {
        form.setErrors(error.fieldErrors);
        failure = form.errorCount === 0 ? (error.formErrors[0] ?? error.messages[0] ?? null) : null;
        if (root !== null) await form.focusFirstInvalid(root);
      } else {
        failure = describeFailure(error).message;
      }
    } finally {
      busy = false;
    }
  }

  // Throws away what was typed for what someone else saved.
  async function reload(): Promise<void> {
    if (tagId === null) return;
    const fresh = await tagApi.get({ tagId });
    version = fresh.version;
    form.reset(tagForm.fromResponse(fresh));
    conflict = false;
  }

  async function removed(): Promise<void> {
    form.reset();
    await invalidate('app:tags');
    await goto(listHref(search));
  }
</script>

<!-- "Admin > Tags > Communication": the list is one step back, at the page it was on, and the tag is where you are. -->
<Crumbs items={[{ label: 'Tags', href: listHref(search) }, { label: tag?.name ?? 'New tag' }]} />

<svelte:head>
  <title>{isNew ? 'New tag' : (tag?.name ?? 'Tag')} · Tags · Admin</title>
</svelte:head>

<!-- The sample in the color picker's contrast table: the chip a tag is, with the name being edited. -->
{#snippet namePreview({ background, color }: PreviewColors)}
  <span
    class="inline-block max-w-[14rem] truncate rounded-full px-2.5 py-0.5 align-middle text-xs font-medium"
    style:background-color={background}
    style:color
  >
    {form.values.name.trim() === '' ? 'Preview' : form.values.name.trim()}
  </span>
{/snippet}

<div class="mx-auto grid max-w-3xl gap-6 p-4 md:p-6" bind:this={root}>
  <div class="space-y-1">
    <h1 class="text-2xl font-semibold tracking-tight" data-pane-heading tabindex="-1">
      {isNew ? 'New tag' : (tag?.name ?? 'Tag')}
    </h1>
    <p class="text-sm text-muted-foreground">
      {isNew
        ? 'A name, a slug, and optionally a color, aliases, and a description.'
        : 'Edit the tag and see what uses it.'}
    </p>
  </div>

  {#if conflict}
    <Alert.Root variant="destructive" role="alert">
      <Alert.Title>Someone saved this tag after you opened it</Alert.Title>
      <Alert.Description>
        <p>
          Saving now would undo their changes. Load their version (and lose what you typed here), or save yours over it.
        </p>
        <div class="mt-3 flex gap-2">
          <Button type="button" variant="outline" size="sm" onclick={reload}>Load their version</Button>
          <Button
            type="button"
            variant="destructive"
            size="sm"
            onclick={() => submit({ again: false, overwrite: true })}
          >
            Save mine anyway
          </Button>
        </div>
      </Alert.Description>
    </Alert.Root>
  {/if}
  {#if failure !== null}
    <Alert.Root variant="destructive" role="alert"><Alert.Description>{failure}</Alert.Description></Alert.Root>
  {/if}

  <div>
    {#if tagId !== null}
      <TabLinks
        label="Sections of the tag"
        tabs={[
          { label: 'Details', href: paneHref(tagId, search), active: tab === 'details' },
          { label: 'Used by', href: paneHref(tagId, search, 'used-by'), active: tab === 'used-by' },
        ]}
      />
    {/if}

    <!-- Both sections stay in the page, so the form is there to submit from the other one. -->
    <div hidden={tab !== 'details'} class="pt-4">
      <form id="tag-form" class="grid gap-6" novalidate onsubmit={save}>
        <Field
          name="name"
          label="Name"
          errors={form.errors['name']}
          description="What the tag is called. Names are unique, ignoring case."
        >
          {#snippet children({ id, describedBy, invalid }: FieldControl)}
            <Input
              {...noAutofill}
              {id}
              value={form.values.name}
              oninput={changeName}
              aria-describedby={describedBy}
              aria-invalid={invalid}
              autocomplete="off"
            />
          {/snippet}
        </Field>

        <Field
          name="slug"
          label="Slug"
          errors={form.errors['slug']}
          description="The tag as it appears in a URL: lowercase words joined by hyphens."
        >
          {#snippet children({ id, describedBy, invalid }: FieldControl)}
            <Input
              {...noAutofill}
              {id}
              class="font-mono"
              value={form.values.slug}
              oninput={(event) => {
                form.values.slug = event.currentTarget.value;
                form.clear('slug');
              }}
              aria-describedby={describedBy}
              aria-invalid={invalid}
              autocomplete="off"
              spellcheck="false"
            />
          {/snippet}
        </Field>
        {#if slugChanged && tag !== null}
          <p class="-mt-4 text-sm text-muted-foreground">
            The old slug, <code class="font-mono">{tag.slug}</code>, keeps working: it redirects to the new one.
          </p>
        {/if}
        {#if form.values.previousSlugs.length > 0}
          <div class="-mt-2 grid gap-2">
            <p class="text-sm font-medium">Previous slugs</p>
            <ul class="grid gap-1.5">
              {#each form.values.previousSlugs as previous (previous)}
                <li class="flex items-center gap-3 text-sm">
                  <code class="font-mono">{previous}</code>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onclick={() =>
                      (form.values.previousSlugs = form.values.previousSlugs.filter((slug) => slug !== previous))}
                  >
                    Remove<span class="sr-only"> {previous}</span>
                  </Button>
                </li>
              {/each}
            </ul>
            <p class="text-sm text-muted-foreground">A removed slug stops redirecting, and any tag can then use it.</p>
          </div>
        {/if}

        <Field
          name="color"
          label="Color"
          errors={form.errors['color']}
          description="The background in light mode. Dark mode uses the mirrored shade."
        >
          {#snippet children({ id, describedBy, invalid }: FieldControl)}
            <ColorPicker
              bind:value={form.values.color}
              preview={namePreview}
              {id}
              {describedBy}
              {invalid}
              onchange={() => form.clear('color')}
            />
          {/snippet}
        </Field>

        <!-- TEMP: remove with temporaryColor when the optional picker has been tried. -->
        <Field
          name="temporary-color"
          label="Optional color (temporary)"
          description="Only to try the picker when a color is optional. Nothing here is saved."
        >
          {#snippet children({ id, describedBy, invalid }: FieldControl)}
            <ColorPicker bind:value={temporaryColor} preview={namePreview} allowNone {id} {describedBy} {invalid} />
          {/snippet}
        </Field>

        <Field
          name="description"
          label="Description"
          errors={form.errors['description']}
          description="Markdown. It shows when the tag is hovered."
        >
          {#snippet children({ id, describedBy, invalid }: FieldControl)}
            <Textarea
              {...noAutofill}
              {id}
              rows={5}
              bind:value={form.values.description}
              oninput={() => form.clear('description')}
              aria-describedby={describedBy}
              aria-invalid={invalid}
            />
          {/snippet}
        </Field>

        <div class="grid gap-2">
          <p class="text-sm font-medium" id="aliases-label">Aliases</p>
          <p class="text-sm text-muted-foreground">Other names for the tag, such as JS for JavaScript.</p>
          {#each form.values.aliases as _, index (index)}
            {@const field = `aliases[${index.toString()}]`}
            <div class="grid gap-1">
              <div class="flex items-center gap-2">
                <Input
                  {...noAutofill}
                  data-field={field}
                  bind:value={form.values.aliases[index]}
                  oninput={() => form.clear(field)}
                  aria-labelledby="aliases-label"
                  aria-invalid={(form.errors[field] ?? []).length > 0}
                  autocomplete="off"
                />
                <Button type="button" variant="ghost" size="sm" onclick={() => removeAlias(index)}>
                  Remove<span class="sr-only"> alias {index + 1}</span>
                </Button>
              </div>
              {#if (form.errors[field] ?? []).length > 0}
                <p class="text-sm text-destructive">{form.errors[field]?.join(' ')}</p>
              {/if}
            </div>
          {/each}
          <Button type="button" variant="outline" size="sm" class="w-fit" onclick={addAlias}>+ Add alias</Button>
        </div>
      </form>
    </div>

    {#if !isNew}
      <div hidden={tab !== 'used-by'} class="pt-4">
        {#if usage === null || usage.warnings.length === 0}
          <p class="text-sm text-muted-foreground">Nothing uses this tag yet.</p>
        {:else}
          <ul class="grid gap-2 text-sm">
            {#each usage.warnings as item (item.kind)}
              {@const words = impactKinds[item.kind]}
              <li class="flex items-center gap-2">
                <words.icon class="size-4 text-muted-foreground" aria-hidden="true" />
                <a class="underline-offset-4 hover:underline" href={words.href}>{describeImpactItem(item)}</a>
              </li>
            {/each}
          </ul>
        {/if}
      </div>
    {/if}
  </div>
</div>

<PaneBar
  formId="tag-form"
  saveLabel="Save tag"
  summary={form.summary}
  isDirty={form.isDirty}
  isBusy={busy}
  onCancel={() => goto(listHref(search))}
>
  {#snippet extra()}
    {#if isNew}
      <Button type="submit" form="tag-form" variant="secondary" data-again="true" disabled={busy}
        >Save and add another</Button
      >
    {/if}
  {/snippet}
  {#snippet danger()}
    {#if !isNew}
      <Button type="button" variant="destructive" onclick={() => (confirmingDelete = true)}>Delete</Button>
    {/if}
  {/snippet}
</PaneBar>

<UnsavedChangesDialog {guard} />

{#if tagId !== null && tag !== null}
  <ConfirmDelete
    bind:open={confirmingDelete}
    noun="tag"
    name={tag.name}
    confirmPhrase={`delete-${tag.slug}`}
    loadImpact={() => tagApi.getDeletionImpact({ tagId })}
    remove={() => tagApi.delete({ tagId })}
    onDeleted={removed}
  />
{/if}
