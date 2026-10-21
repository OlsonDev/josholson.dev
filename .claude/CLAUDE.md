# Project

- Personal website for Josh Olson
- www.josholson.dev
- api.josholson.dev

# Tech

- Proxmox, Linux
- Caddy proxy
- Podman
- PostgreSQL
- C# / .NET
  - Structured logging
  - OpenTelemetry
  - Dapper
  - Waypoint CLI migrations
- Bun
  - TypeScript
  - SvelteKit
  - Playwright

# Podman

- compose.yaml
- PostgresSQL
- C# API
- SvelteKit SSR

# Database tables - superset of JSON Resume

- Basics
- Work
- Education
- Skill
- Project
- Language
- Interest
- Reference

# User experience
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
- Contact page
  - Obfuscated email address
  - Contact form
    - Stored in database
    - Email sent to me
- Secret admin page
  - Secure auth
  - Contact form submission list page
  - Editors for JSON Resume
