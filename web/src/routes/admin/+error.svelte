<script lang="ts">
  import * as Alert from '#lib/components/ui/alert/index.ts';
  import { page } from '$app/state';
</script>

<svelte:head>
  <title>Admin</title>
  <meta name="robots" content="noindex" />
</svelte:head>

<div class="flex min-h-svh items-center justify-center p-4">
  <Alert.Root variant="destructive" class="max-w-md" role="alert">
    <Alert.Title>{page.status === 503 ? 'The API is not answering' : 'Something went wrong'}</Alert.Title>
    <Alert.Description>
      {page.error?.message}
      <a href="/admin" class="underline underline-offset-4">Try again</a>
    </Alert.Description>
  </Alert.Root>
</div>
