<script lang="ts">
  import { passkey } from '#lib/api/index.ts';
  import { attempt } from '#lib/auth/failure.ts';
  import { describePasskey, describeWhen, nicknameProblem } from '#lib/auth/passkeys.ts';
  import { canUsePhone, createPasskey, signWithPasskey, supportsPasskeys, type CreateHint } from '#lib/auth/webauthn.ts';
  import { refreshAll } from '$app/navigation';
  import { onMount } from 'svelte';
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

<svelte:head>
  <title>Passkeys</title>
  <meta name="robots" content="noindex" />
</svelte:head>

<p><a href="/admin">Admin</a></p>
<h1>Passkeys</h1>

{#if !data.apiAvailable}
  <p role="alert">The API is not answering, so nothing here can work right now.</p>
{:else if !supportsPasskeys()}
  <p role="alert">This browser cannot use passkeys. Use a current Chrome, Firefox, or Safari.</p>
{:else}
  <table>
    <caption>Passkeys that can sign in</caption>
    <thead>
      <tr>
        <th scope="col">Name</th>
        <th scope="col">Kind</th>
        <th scope="col">Added</th>
        <th scope="col">Last used</th>
        <th scope="col"><span class="visually-hidden">Actions</span></th>
      </tr>
    </thead>
    <tbody>
      {#each data.passkeys as item (item.passkeyId)}
        <tr>
          <th scope="row">
            {#if renaming?.passkeyId === item.passkeyId}
              <form
                onsubmit={(event) => {
                  event.preventDefault();
                  void rename();
                }}
              >
                <label>
                  <span class="visually-hidden">New name for {item.nickname}</span>
                  <input bind:value={renaming.nickname} required maxlength="64" autocomplete="off" />
                </label>
                <button type="submit" disabled={busy}>Save</button>
                <button type="button" disabled={busy} onclick={() => (renaming = null)}>Cancel</button>
                {#each renameErrors as error (error)}
                  <p class="error">{error}</p>
                {/each}
              </form>
            {:else}
              {item.nickname}
            {/if}
          </th>
          <td>{describePasskey(item)}</td>
          <td>{describeWhen(item.createdUtc)}</td>
          <td>{describeWhen(item.lastUsedUtc)}</td>
          <td>
            {#if removing === item.passkeyId}
              <button type="button" disabled={busy} onclick={() => remove(item.passkeyId)}>
                Remove {item.nickname}?
              </button>
              <button type="button" disabled={busy} onclick={() => (removing = null)}>Keep</button>
            {:else if renaming?.passkeyId !== item.passkeyId}
              <button
                type="button"
                disabled={busy}
                onclick={() => {
                  renameErrors = [];
                  renaming = { passkeyId: item.passkeyId, nickname: item.nickname };
                }}
              >
                Rename
              </button>
              <button
                type="button"
                disabled={busy || onlyOne}
                title={onlyOne ? 'The only passkey cannot be removed' : undefined}
                onclick={() => (removing = item.passkeyId)}
              >
                Remove
              </button>
            {/if}
          </td>
        </tr>
      {/each}
    </tbody>
  </table>

  <section aria-labelledby="add-heading">
    <h2 id="add-heading">Add a passkey</h2>
    <p>Name it first. Adding one asks for a passkey you already have as well, to confirm.</p>
    <label>
      What to call it
      <input bind:value={newNickname} maxlength="64" autocomplete="off" aria-invalid={newNicknameErrors.length > 0} />
    </label>
    {#each newNicknameErrors as error (error)}
      <p class="error">{error}</p>
    {/each}
    <button type="button" disabled={busy} onclick={() => add('security-key')}>Add a security key</button>
    <button type="button" disabled={busy || !phoneSupported} onclick={() => add('hybrid')}>Add a phone</button>
    {#if !phoneSupported}
      <p>This browser cannot add a phone. Use Chrome or Edge, or a security key.</p>
    {/if}
  </section>
{/if}

{#if status !== null}
  <p role="status">{status}</p>
{/if}
{#if message !== null}
  <p role="alert" class="error">{message}</p>
{/if}

<style>
  table {
    width: 100%;
    border-collapse: collapse;
  }

  th,
  td {
    padding: var(--space-2);
    border-bottom: 1px solid var(--border);
    text-align: left;
  }

  caption {
    text-align: left;
    font-weight: bold;
    padding-bottom: var(--space-2);
  }

  label {
    display: block;
    margin-bottom: var(--space-3);
  }

  input {
    display: block;
    margin-top: var(--space-1);
  }

  .error {
    color: var(--error, #b00020);
  }

  .visually-hidden {
    position: absolute;
    width: 1px;
    height: 1px;
    overflow: hidden;
    clip-path: inset(50%);
    white-space: nowrap;
  }
</style>
