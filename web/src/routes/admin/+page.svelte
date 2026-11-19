<script lang="ts">
  import { enrollment, signIn } from '#lib/api/index.ts';
  import { attempt } from '#lib/auth/failure.ts';
  import { createPasskey, signWithPasskey, supportsPasskeys } from '#lib/auth/webauthn.ts';
  import { refreshAll } from '$app/navigation';
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();

  let setupSecret = $state('');
  let displayName = $state('');
  let nickname = $state('');
  let busy = $state(false);
  let message = $state<string | null>(null);
  let fields = $state<Readonly<Record<string, readonly string[]>>>({});

  // Which of the three pages this is: signed in, setting up (the API says no passkey exists yet), or signing in.
  const view = $derived(data.user !== null ? 'signed-in' : data.setup?.available === true ? 'setup' : 'sign-in');

  // Runs one thing the visitor asked for, shows what went wrong in words, and reloads what the page knows when it worked.
  async function run(action: () => Promise<void>, messages: Partial<Record<number, string>> = {}): Promise<void> {
    busy = true;
    message = null;
    fields = {};
    const failure = await attempt(action, messages);
    if (failure === null) await refreshAll();
    else {
      message = failure.message;
      fields = failure.fields;
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

  // The secret buys a one-time token, and the token registers the passkey. The API accepts the secret only while no
  // passkey exists, so this page stops being offered with the first one.
  const setUp = (): Promise<void> =>
    run(
      async () => {
        const name = displayName.trim();
        const { token } = await enrollment.setup({ setupSecret, displayName: name === '' ? undefined : name });
        const { attemptToken, options } = await enrollment.options({ token, nickname });
        const response = await createPasskey(options);
        await enrollment.enroll({ token, attemptToken, response });
        setupSecret = '';
      },
      {
        404: 'That is not the setup secret, or setup is already done.',
        400: 'The passkey did not verify.',
        422: 'Check the highlighted fields.',
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
{:else if view === 'setup'}
  <section aria-labelledby="setup-heading">
    <h2 id="setup-heading">Set up</h2>
    <p>
      No passkey is registered yet. Enter the setup secret the API was started with, then register the first passkey with a
      security key or this device.
    </p>
    <form
      onsubmit={(event) => {
        event.preventDefault();
        void setUp();
      }}
    >
      <label>
        Setup secret
        <input type="password" bind:value={setupSecret} required autocomplete="off" />
      </label>
      {#if data.setup?.needsDisplayName === true}
        <label>
          Your name
          <input
            bind:value={displayName}
            required
            maxlength="64"
            autocomplete="name"
            aria-invalid={(fields['displayName'] ?? []).length > 0}
          />
        </label>
        {#each fields['displayName'] ?? [] as error (error)}
          <p class="error">{error}</p>
        {/each}
      {/if}
      <label>
        What to call this passkey
        <input
          bind:value={nickname}
          required
          maxlength="64"
          autocomplete="off"
          aria-invalid={(fields['nickname'] ?? []).length > 0}
        />
      </label>
      {#each fields['nickname'] ?? [] as error (error)}
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
