import { defineEnvVars } from '@sveltejs/kit/env';

export const variables = defineEnvVars({
  API_URL: {
    description: 'The API as the web server reaches it: the compose network in production, localhost in development',
    schema: (value) => value ?? 'http://localhost:5000',
  },
});
