# Product

What the site does. Pages and features:

- Homepage
  - Avatar photo
  - Skill highlights
- Résumé
  - HTML version
  - PDF version
  - JSON Resume version
  - Single source of truth
    - CSS for differences
- Skills
  - Card list page with filters
    - Name
    - Icon
    - Years used
    - Versions used (freeform)
    - 5-star rating (half stars allowed)
    - Tags
    - Notes
- Projects
- Blog
- Contact page
  - Obfuscated email address
  - Contact form
    - Stored in database
    - Email sent to me
- Secret admin page (`/admin`, behind a sidebar of one level: Dashboard, Inbox, Resumes, Skills, Projects, Blog, Tags, Media)
  - Sign in with a passkey: Red, Purple, or a phone (no password), on a page of its own (`/admin/sign-in`), which is also where the first passkey is set up
  - Passkey page (`/admin/passkeys`, under the user's name with Sign out): list, add a phone or a security key, rename, and remove, each change confirmed with a passkey already registered
  - Inbox: the contact form submission list
  - Resumes: editors for JSON Resume, one resume and its sections at a time. Skills and Projects are the default resume's own, with pages of their own
  - Dashboard: what needs attention, with a card for each feature as it exists
