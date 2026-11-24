import { defineParams } from '@sveltejs/kit/params';

export const params = defineParams({
  // The last part of the URL of an edit pane: the ID of what is edited, or `+` for a new one (`/admin/tags/5`,
  // `/admin/tags/+`). Anything else is not a pane, so it is a 404 and not a request for a tag called "foo".
  paneid: (param) => (param === '+' || /^\d+$/.test(param) ? param : undefined),
});
