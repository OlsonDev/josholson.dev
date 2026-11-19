// The admin pages are drawn in the browser. The session cookie is the browser's, and the API takes it directly (ADR 0027:
// passkey auth and direct browser-to-API calls), so a server render would have nothing to show but "signed out".
export const ssr = false;
