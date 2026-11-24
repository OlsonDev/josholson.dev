import { defineEnvVars } from '@sveltejs/kit/env';

export const variables = defineEnvVars({
  API_URL: {
    description: 'The API as the web server reaches it: the compose network in production, localhost in development',
    schema: (value) => value ?? 'http://localhost:5000',
  },
  TEST_SCOPES: {
    description:
      "Set to 'true' by the browser tests' web server: a page request's test scope header is passed on to the calls it makes to the API, so a page drawn on the server reads the test's own database. Never set in production",
    schema: (value) => value === 'true',
  },
  PUBLIC_API_URL: {
    public: true,
    description: 'The API as the browser reaches it: api.josholson.dev in production, localhost in development',
    schema: (value) => value ?? 'http://localhost:5000',
  },
});
