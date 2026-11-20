<script lang="ts">
  import { enrollment, signIn } from '#lib/api/index.ts';
  import { attempt } from '#lib/auth/failure.ts';
  import { createPasskey, signWithPasskey, supportsPasskeys } from '#lib/auth/webauthn.ts';
  import * as Alert from '#lib/components/ui/alert/index.ts';
  import { Button } from '#lib/components/ui/button/index.ts';
  import * as Card from '#lib/components/ui/card/index.ts';
  import { Input } from '#lib/components/ui/input/index.ts';
  import { Label } from '#lib/components/ui/label/index.ts';
  import { goto } from '$app/navigation';
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();

  let setupSecret = $state('');
  let displayName = $state('');
  let nickname = $state('');
  let busy = $state(false);
  let message = $state<string | null>(null);
  let fields = $state<Readonly<Record<string, readonly string[]>>>({});

  const setupOpen = $derived(data.setup?.available === true);

  // Runs one thing the visitor asked for and shows what went wrong in words. When it worked, the visitor is signed in, and
  // the admin area is where they go.
  async function run(action: () => Promise<void>, messages: Partial<Record<number, string>> = {}): Promise<void> {
    busy = true;
    message = null;
    fields = {};
    const failure = await attempt(action, messages);
    if (failure === null) await goto('/admin', { invalidateAll: true });
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
  // passkey exists, so this form stops being offered with the first one.
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
</script>

<svelte:head>
  <title>{setupOpen ? 'Set up' : 'Sign in'} · Admin</title>
  <meta name="robots" content="noindex" />
</svelte:head>

<div class="flex min-h-svh items-center justify-center p-4">
  <Card.Root class="w-full max-w-sm">
    {#if !data.apiAvailable}
      <Card.Header>
        <Card.Title><h1 class="text-xl">Admin</h1></Card.Title>
      </Card.Header>
      <Card.Content>
        <Alert.Root variant="destructive" role="alert">
          <Alert.Description>The API is not answering, so nothing here can work right now.</Alert.Description>
        </Alert.Root>
      </Card.Content>
    {:else if !supportsPasskeys()}
      <Card.Header>
        <Card.Title><h1 class="text-xl">Admin</h1></Card.Title>
      </Card.Header>
      <Card.Content>
        <Alert.Root variant="destructive" role="alert">
          <Alert.Description>This browser cannot use passkeys. Use a current Chrome, Firefox, or Safari.</Alert.Description>
        </Alert.Root>
      </Card.Content>
    {:else if setupOpen}
      <Card.Header>
        <Card.Title><h1 class="text-xl">Set up</h1></Card.Title>
        <Card.Description>
          No passkey is registered yet. Enter the setup secret the API was started with, then register the first passkey
          with a security key or this device.
        </Card.Description>
      </Card.Header>
      <Card.Content>
        <form
          class="grid gap-4"
          onsubmit={(event) => {
            event.preventDefault();
            void setUp();
          }}
        >
          <div class="grid gap-2">
            <Label for="setup-secret">Setup secret</Label>
            <Input id="setup-secret" type="password" bind:value={setupSecret} required autocomplete="off" />
          </div>
          {#if data.setup?.needsDisplayName === true}
            <div class="grid gap-2">
              <Label for="display-name">Your name</Label>
              <Input
                id="display-name"
                bind:value={displayName}
                required
                maxlength={64}
                autocomplete="name"
                aria-invalid={(fields['displayName'] ?? []).length > 0}
              />
              {#each fields['displayName'] ?? [] as error (error)}
                <p class="text-sm text-destructive">{error}</p>
              {/each}
            </div>
          {/if}
          <div class="grid gap-2">
            <Label for="nickname">What to call this passkey</Label>
            <Input
              id="nickname"
              bind:value={nickname}
              required
              maxlength={64}
              autocomplete="off"
              aria-invalid={(fields['nickname'] ?? []).length > 0}
            />
            {#each fields['nickname'] ?? [] as error (error)}
              <p class="text-sm text-destructive">{error}</p>
            {/each}
          </div>
          <Button type="submit" disabled={busy}>Register passkey</Button>
        </form>
      </Card.Content>
    {:else}
      <Card.Header>
        <Card.Title><h1 class="text-xl">Sign in</h1></Card.Title>
        <Card.Description>Use a passkey: a security key, a phone, or this device.</Card.Description>
      </Card.Header>
      <Card.Content>
        <Button class="w-full" disabled={busy} onclick={signInWithPasskey}>Sign in with a passkey</Button>
      </Card.Content>
    {/if}
    {#if message !== null}
      <Card.Footer>
        <Alert.Root variant="destructive" role="alert">
          <Alert.Description>{message}</Alert.Description>
        </Alert.Root>
      </Card.Footer>
    {/if}
  </Card.Root>
</div>
