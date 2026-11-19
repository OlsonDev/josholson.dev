import type { CDPSession, Page } from '@playwright/test';

// A passkey the browser makes up, so no key is touched: Chromium's virtual authenticator, which behaves like a security
// key with a PIN that is always right and a touch that happens by itself, until a test says it does not.
export type VirtualAuthenticator = {
  /** How many passkeys it holds. */
  credentialCount: () => Promise<number>;
  /** The names it stored with them (what a browser's chooser shows). */
  userNames: () => Promise<(string | undefined)[]>;
  /** Whether it answers (the touch happens). While it does not, a request that needs it waits. */
  respond: (answering: boolean) => Promise<void>;
};

export async function addVirtualAuthenticator(page: Page): Promise<VirtualAuthenticator> {
  const cdp: CDPSession = await page.context().newCDPSession(page);
  await cdp.send('WebAuthn.enable');
  const { authenticatorId } = await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: {
      protocol: 'ctap2',
      transport: 'usb',
      hasResidentKey: true,
      hasUserVerification: true,
      isUserVerified: true,
      automaticPresenceSimulation: true,
    },
  });
  return {
    credentialCount: async () => (await cdp.send('WebAuthn.getCredentials', { authenticatorId })).credentials.length,
    userNames: async () =>
      (await cdp.send('WebAuthn.getCredentials', { authenticatorId })).credentials.map(
        (credential) => credential.userName,
      ),
    respond: async (answering) => {
      await cdp.send('WebAuthn.setAutomaticPresenceSimulation', { authenticatorId, enabled: answering });
    },
  };
}
