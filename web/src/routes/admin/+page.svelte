<script lang="ts">
  import { enrollment, signIn } from '#lib/api/index.ts';
  import { attempt } from '#lib/auth/failure.ts';
  import { createPasskey, signWithPasskey, supportsPasskeys } from '#lib/auth/webauthn.ts';
  import { refreshAll } from '$app/navigation';
  import { onMount } from 'svelte';
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();

  let nickname = $state('');
  let busy = $state(false);
  let message = $state<string | null>(null);
  let nicknameErrors = $state<string[]>([]);

  // The enrollment token that came with the link, `/admin#token=...`: a fragment, which the browser never sends to a
  // server or a log. The page is only drawn in the browser, so it can be read when this runs. It is kept here, not in the
  // address bar, so a reload or a later load of the page does not bring it back.
  let enrollmentToken = $state(new URLSearchParams(location.hash.slice(1)).get('token'));

  onMount(() => {
    if (location.hash !== '') history.replaceState(history.state, '', location.pathname + location.search);
  });

  // Which of the three pages this is: signed in, enrolling (a token came with the link), or signing in.
  const view = $derived(data.user !== null ? 'signed-in' : enrollmentToken !== null ? 'enroll' : 'sign-in');

  // Runs one thing the visitor asked for, shows what went wrong in words, and reloads what the page knows when it worked.
  async function run(action: () => Promise<void>, messages: Partial<Record<number, string>> = {}): Promise<void> {
    busy = true;
    message = null;
    nicknameErrors = [];
    const failure = await attempt(action, messages);
    if (failure === null) await refreshAll();
    else {
      message = failure.message;
      nicknameErrors = [...(failure.fields['nickname'] ?? [])];
    }
    busy = false;
  }

  const signInWithPasskey = (): Promise<void> =>
    run(
      async () => {
        const { attemptToken, options } = await signIn.options();
        const response = await signWithPasskey(options);
        await signIn.signIn({ attemptToken, response });
      },
      { 401: 'That passkey was not accepted.' },
    );

  const enroll = (token: string): Promise<void> =>
    run(
      async () => {
        const { attemptToken, options } = await enrollment.options({ token });
        const response = await createPasskey(options);
        await enrollment.enroll({ token, attemptToken, nickname, response });
        // The token is used up, so signing out later is a sign-in and not another enrollment.
        enrollmentToken = null;
      },
      {
        404: 'This enrollment link has expired or was already used. Make a new one.',
        400: 'The passkey did not verify.',
        422: 'Check the name.',
      },
    );

  const signOut = (): Promise<void> => run(() => signIn.signOut());
</script>

<svelte:head>
  <title>Admin</title>
  <meta name="robots" content="noindex" />
</svelte:head>

<h1>Admin</h1>

{#if !data.apiAvailable}
  <p role="alert">The API is not answering, so nothing here can work right now.</p>
{:else if !supportsPasskeys()}
  <p role="alert">This browser cannot use passkeys. Use a current Chrome, Firefox, or Safari.</p>
{:else if view === 'signed-in' && data.user !== null}
  <p>Signed in as {data.user.displayName}.</p>
  <nav aria-label="Admin">
    <a href="/admin/passkeys">Passkeys</a>
  </nav>
  <button type="button" disabled={busy} onclick={signOut}>Sign out</button>
{:else if view === 'enroll' && enrollmentToken !== null}
  <section aria-labelledby="enroll-heading">
    <h2 id="enroll-heading">Register a passkey</h2>
    <p>This link is for registering a passkey on this account. Insert a security key or use this device.</p>
    <form
      onsubmit={(event) => {
        event.preventDefault();
        void enroll(enrollmentToken ?? '');
      }}
    >
      <label>
        What to call it
        <input bind:value={nickname} required maxlength="64" autocomplete="off" aria-invalid={nicknameErrors.length > 0} />
      </label>
      {#each nicknameErrors as error (error)}
        <p class="error">{error}</p>
      {/each}
      <button type="submit" disabled={busy}>Register passkey</button>
    </form>
  </section>
{:else}
  <section aria-labelledby="sign-in-heading">
    <h2 id="sign-in-heading">Sign in</h2>
    <button type="button" disabled={busy} onclick={signInWithPasskey}>Sign in with a passkey</button>
  </section>
{/if}

{#if message !== null}
  <p role="alert" class="error">{message}</p>
{/if}

<style>
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
</style>
