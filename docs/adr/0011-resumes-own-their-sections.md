# ADR 0011: Resumes own their sections

- Status: Accepted
- Date: 2026-10-21

## Context

Tailored resumes such as `/resume/customized-for-employer-foo` may show different jobs, a different order, and different highlights.

## Decision

Every section table has a `resume_id` and its rows belong to exactly one resume. Cloning a resume copies its rows. Each row has `is_visible` so an entry can be hidden on one resume without being deleted. Where children reference each other (a skill and its group), a composite foreign key keeps them within the same resume.

## Alternatives considered

- Shared master rows plus per-resume join tables with ordering and overrides: avoids drift, but adds many joins and a more complex editor.

## Consequences

- A typo fixed in one resume is not fixed in the others.
- The Skills page shows the default resume's skills.
- Deleting a resume cascades to all its sections.
