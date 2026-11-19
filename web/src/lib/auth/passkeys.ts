import { PasskeyTransport, type PasskeyResponse } from '#lib/api/schema.ts';

/** What kind of authenticator a passkey is, in words, from how it said it can be reached (ADR 0031: Red, Purple, or a phone). */
export function describePasskey(passkey: Pick<PasskeyResponse, 'transports' | 'isBackedUp'>): string {
  const { transports } = passkey;
  const kind = transports.includes(PasskeyTransport.Hybrid)
    ? 'Phone'
    : transports.some(
          (transport) =>
            transport === PasskeyTransport.Usb ||
            transport === PasskeyTransport.Nfc ||
            transport === PasskeyTransport.Ble ||
            transport === PasskeyTransport.SmartCard,
        )
      ? 'Security key'
      : transports.includes(PasskeyTransport.Internal)
        ? 'This device'
        : 'Passkey';
  return passkey.isBackedUp ? `${kind}, synced` : kind;
}

/** The first thing wrong with a nickname the API would refuse, or null: nothing is made until it is right. */
export function nicknameProblem(nickname: string, others: readonly string[]): string | null {
  const trimmed = nickname.trim();
  if (trimmed === '') return 'Give it a name.';
  if (trimmed.length > 64) return 'The name can be at most 64 characters.';
  if (others.includes(trimmed)) return 'Another passkey already has that name.';
  return null;
}

/** When something happened, in the visitor's own locale and time zone, or a dash for never. */
export const describeWhen = (instant: Temporal.Instant | null): string =>
  instant === null ? '–' : instant.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
