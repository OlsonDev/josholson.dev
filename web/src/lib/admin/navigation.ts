import type { Component } from 'svelte';
import FileTextIcon from '@lucide/svelte/icons/file-text';
import FolderKanbanIcon from '@lucide/svelte/icons/folder-kanban';
import ImageIcon from '@lucide/svelte/icons/image';
import InboxIcon from '@lucide/svelte/icons/inbox';
import KeyRoundIcon from '@lucide/svelte/icons/key-round';
import LayoutDashboardIcon from '@lucide/svelte/icons/layout-dashboard';
import NewspaperIcon from '@lucide/svelte/icons/newspaper';
import SparklesIcon from '@lucide/svelte/icons/sparkles';
import TagIcon from '@lucide/svelte/icons/tag';

/** A page in the admin area, as the sidebar and the breadcrumb know it. */
export type AdminPage = { title: string; href: string; icon: Component };

/** The sidebar's pages, in order. One level: a page with pages of its own (a resume's sections) shows them itself. */
export const adminPages: readonly AdminPage[] = [
  { title: 'Dashboard', href: '/admin', icon: LayoutDashboardIcon },
  { title: 'Inbox', href: '/admin/inbox', icon: InboxIcon },
  { title: 'Resumes', href: '/admin/resumes', icon: FileTextIcon },
  { title: 'Skills', href: '/admin/skills', icon: SparklesIcon },
  { title: 'Projects', href: '/admin/projects', icon: FolderKanbanIcon },
  { title: 'Blog', href: '/admin/blog', icon: NewspaperIcon },
  { title: 'Tags', href: '/admin/tags', icon: TagIcon },
  { title: 'Media', href: '/admin/media', icon: ImageIcon },
];

/** Under the user's name, not in the list: a page about the account and not the site's content. */
export const passkeysPage: AdminPage = { title: 'Passkeys', href: '/admin/passkeys', icon: KeyRoundIcon };

const allPages = [...adminPages, passkeysPage];

/** Whether `pathname` is `page` or something under it. The dashboard is only itself, or it would be every page. */
export const isCurrent = (page: AdminPage, pathname: string): boolean =>
  page.href === '/admin' ? pathname === '/admin' : pathname === page.href || pathname.startsWith(`${page.href}/`);

/** The page `pathname` is, for the breadcrumb. */
export const pageAt = (pathname: string): AdminPage | undefined => allPages.find((page) => isCurrent(page, pathname));
