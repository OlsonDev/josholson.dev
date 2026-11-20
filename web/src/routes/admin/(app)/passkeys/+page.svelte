<script lang="ts">
  import { refreshAll } from '$app/navigation';
  import { onMount } from 'svelte';

  import { passkey } from '#lib/api/index.ts';
  import { attempt } from '#lib/auth/failure.ts';
  import { describePasskey, describeWhen, nicknameProblem } from '#lib/auth/passkeys.ts';
  import {
    canUsePhone,
    type CreateHint,
    createPasskey,
    signWithPasskey,
    supportsPasskeys,
  } from '#lib/auth/webauthn.ts';
  import PageHeader from '#lib/components/page-header.svelte';
  import * as Alert from '#lib/components/ui/alert/index.ts';
  import { Button } from '#lib/components/ui/button/index.ts';
  import * as Card from '#lib/components/ui/card/index.ts';
  import { Input } from '#lib/components/ui/input/index.ts';
  import { Label } from '#lib/components/ui/label/index.ts';
  import * as Table from '#lib/components/ui/table/index.ts';

  import type { PageProps } from './$types';

  let { data }: PageProps = $props();

  let busy = $state(false);
  let status = $state<string | null>(null);
  let message = $state<string | null>(null);
  let newNickname = $state('');
  let newNicknameErrors = $state<string[]>([]);
  let renaming = $state<{ passkeyId: number; nickname: string } | null>(null);
  let renameErrors = $state<string[]>([]);
  let removing = $state<number | null>(null);

  // Whether the browser can make a passkey on a phone. It is asked once, and the button is there until it says no.
  let phoneSupported = $state(true);
  onMount(async () => {
    phoneSupported = await canUsePhone();
  });

  const onlyOne = $derived(data.passkeys.length < 2);
  const namesExcept = (passkeyId: number | null): string[] =>
    data.passkeys.filter((existing) => existing.passkeyId !== passkeyId).map((existing) => existing.nickname);

  // Runs one thing the visitor asked for. Null when it worked, and then what the page knows is loaded again.
  async function run(
    action: () => Promise<void>,
    messages: Partial<Record<number, string>> = {},
  ): Promise<Readonly<Record<string, readonly string[]>> | null> {
    busy = true;
    message = null;
    const failure = await attempt(action, {
      400: 'That was not accepted, so nothing changed.',
      ...messages,
    });
    if (failure === null) await refreshAll();
    else message = failure.message;
    status = null;
    busy = false;
    return failure === null ? null : failure.fields;
  }

  // Every change to the set of passkeys is confirmed with one that is already registered (ADR 0031): the new one can
  // never confirm itself.
  async function confirm(changeToken: string): Promise<void> {
    status = 'Now confirm with a passkey you already have.';
    const { attemptToken, options } = await passkey.confirmationOptions({ changeToken });
    const response = await signWithPasskey(options);
    await passkey.confirm({ changeToken, attemptToken, response });
  }

  async function add(hint: CreateHint): Promise<void> {
    // Checked here for a quick answer. The API checks the name too, before the key makes anything.
    const problem = nicknameProblem(newNickname, namesExcept(null));
    newNicknameErrors = problem === null ? [] : [problem];
    if (problem !== null) return;
    const fields = await run(async () => {
      status = hint === 'hybrid' ? 'Scan the code with your phone.' : 'Use the security key.';
      const { attemptToken, options } = await passkey.registrationOptions({ nickname: newNickname.trim() });
      const response = await createPasskey(options, hint);
      const { changeToken } = await passkey.register({ attemptToken, response });
      await confirm(changeToken);
      newNickname = '';
    });
    newNicknameErrors = [...(fields?.['nickname'] ?? [])];
  }

  async function remove(passkeyId: number): Promise<void> {
    await run(
      async () => {
        const { changeToken } = await passkey.remove({ passkeyId });
        await confirm(changeToken);
        removing = null;
      },
      {
        404: 'That passkey is already gone.',
        409: 'It is the only passkey, and removing it would lock you out.',
      },
    );
  }

  async function rename(): Promise<void> {
    if (renaming === null) return;
    const { passkeyId, nickname } = renaming;
    const problem = nicknameProblem(nickname, namesExcept(passkeyId));
    renameErrors = problem === null ? [] : [problem];
    if (problem !== null) return;
    const fields = await run(
      async () => {
        await passkey.rename({ passkeyId }, { nickname: nickname.trim() });
        renaming = null;
      },
      { 404: 'That passkey is already gone.' },
    );
    renameErrors = [...(fields?.['nickname'] ?? [])];
  }
</script>

<PageHeader
  title="Passkeys"
  description="What can sign in. Adding or removing one is confirmed with a passkey you already have."
/>

{#if !data.apiAvailable}
  <Alert.Root variant="destructive" role="alert">
    <Alert.Description>The API is not answering, so nothing here can work right now.</Alert.Description>
  </Alert.Root>
{:else if !supportsPasskeys()}
  <Alert.Root variant="destructive" role="alert">
    <Alert.Description>This browser cannot use passkeys. Use a current Chrome, Firefox, or Safari.</Alert.Description>
  </Alert.Root>
{:else}
  <Card.Root>
    <Card.Header>
      <Card.Title>Passkeys that can sign in</Card.Title>
    </Card.Header>
    <Card.Content>
      <Table.Root>
        <Table.Header>
          <Table.Row>
            <Table.Head>Name</Table.Head>
            <Table.Head>Kind</Table.Head>
            <Table.Head>Added</Table.Head>
            <Table.Head>Last used</Table.Head>
            <Table.Head><span class="sr-only">Actions</span></Table.Head>
          </Table.Row>
        </Table.Header>
        <Table.Body>
          {#each data.passkeys as item (item.passkeyId)}
            <Table.Row>
              <Table.Head scope="row" class="font-medium text-foreground">
                {#if renaming?.passkeyId === item.passkeyId}
                  <form
                    class="flex flex-wrap items-center gap-2"
                    onsubmit={(event) => {
                      event.preventDefault();
                      void rename();
                    }}
                  >
                    <Label for="rename-{item.passkeyId}" class="sr-only">New name for {item.nickname}</Label>
                    <Input
                      id="rename-{item.passkeyId}"
                      class="w-48"
                      bind:value={renaming.nickname}
                      required
                      maxlength={64}
                      autocomplete="off"
                    />
                    <Button type="submit" size="sm" disabled={busy}>Save</Button>
                    <Button type="button" size="sm" variant="ghost" disabled={busy} onclick={() => (renaming = null)}>
                      Cancel
                    </Button>
                    {#each renameErrors as error (error)}
                      <p class="w-full text-sm text-destructive">{error}</p>
                    {/each}
                  </form>
                {:else}
                  {item.nickname}
                {/if}
              </Table.Head>
              <Table.Cell>{describePasskey(item)}</Table.Cell>
              <Table.Cell>{describeWhen(item.createdUtc)}</Table.Cell>
              <Table.Cell>{describeWhen(item.lastUsedUtc)}</Table.Cell>
              <Table.Cell class="text-right">
                {#if removing === item.passkeyId}
                  <Button variant="destructive" size="sm" disabled={busy} onclick={() => remove(item.passkeyId)}>
                    Remove {item.nickname}?
                  </Button>
                  <Button variant="ghost" size="sm" disabled={busy} onclick={() => (removing = null)}>Keep</Button>
                {:else if renaming?.passkeyId !== item.passkeyId}
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={busy}
                    onclick={() => {
                      renameErrors = [];
                      renaming = { passkeyId: item.passkeyId, nickname: item.nickname };
                    }}
                  >
                    Rename
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={busy || onlyOne}
                    title={onlyOne ? 'The only passkey cannot be removed' : undefined}
                    onclick={() => (removing = item.passkeyId)}
                  >
                    Remove
                  </Button>
                {/if}
              </Table.Cell>
            </Table.Row>
          {/each}
        </Table.Body>
      </Table.Root>
    </Card.Content>
  </Card.Root>

  <Card.Root>
    <Card.Header>
      <Card.Title><h2>Add a passkey</h2></Card.Title>
      <Card.Description
        >Name it first. Adding one asks for a passkey you already have as well, to confirm.</Card.Description
      >
    </Card.Header>
    <Card.Content class="grid max-w-sm gap-4">
      <div class="grid gap-2">
        <Label for="new-nickname">What to call it</Label>
        <Input
          id="new-nickname"
          bind:value={newNickname}
          maxlength={64}
          autocomplete="off"
          aria-invalid={newNicknameErrors.length > 0}
        />
        {#each newNicknameErrors as error (error)}
          <p class="text-sm text-destructive">{error}</p>
        {/each}
      </div>
      <div class="flex flex-wrap gap-2">
        <Button disabled={busy} onclick={() => add('security-key')}>Add a security key</Button>
        <Button variant="outline" disabled={busy || !phoneSupported} onclick={() => add('hybrid')}>Add a phone</Button>
      </div>
      {#if !phoneSupported}
        <p class="text-sm text-muted-foreground">
          This browser cannot add a phone. Use Chrome or Edge, or a security key.
        </p>
      {/if}
    </Card.Content>
  </Card.Root>
{/if}

{#if status !== null}
  <p role="status" class="text-sm text-muted-foreground">{status}</p>
{/if}
{#if message !== null}
  <Alert.Root variant="destructive" role="alert">
    <Alert.Description>{message}</Alert.Description>
  </Alert.Root>
{/if}
