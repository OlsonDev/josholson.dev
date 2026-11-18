import { defineEnvVars } from '@sveltejs/kit/env';

export const variables = defineEnvVars({
  API_URL: {
    description: 'The API as the web server reaches it: the compose network in production, localhost in development',
    schema: (value) => value ?? 'http://localhost:5000',
  },
  PUBLIC_API_URL: {
    public: true,
    description: 'The API as the browser reaches it: api.josholson.dev in production, localhost in development',
    schema: (value) => value ?? 'http://localhost:5000',
  },
});
