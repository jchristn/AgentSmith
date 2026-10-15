// Minimal stroke-icon set. Each icon takes a `size` prop and inherits color.

const base = (size) => ({
  width: size,
  height: size,
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round',
  strokeLinejoin: 'round'
});

export function HomeIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M3 10.5 12 3l9 7.5" />
      <path d="M5 9.5V21h14V9.5" />
    </svg>
  );
}
export function LockIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <rect x="4" y="10" width="16" height="11" rx="2" />
      <path d="M8 10V7a4 4 0 0 1 8 0v3" />
    </svg>
  );
}
export function ActivityIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M3 12h4l3 8 4-16 3 8h4" />
    </svg>
  );
}
export function BuildingIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <rect x="4" y="3" width="16" height="18" rx="1" />
      <path d="M9 8h.01M15 8h.01M9 12h.01M15 12h.01M9 16h6" />
    </svg>
  );
}
export function UsersIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <circle cx="9" cy="8" r="3" />
      <path d="M3 20a6 6 0 0 1 12 0" />
      <path d="M16 5.5a3 3 0 0 1 0 5.4M21 20a6 6 0 0 0-4-5.6" />
    </svg>
  );
}
export function KeyIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <circle cx="7.5" cy="15.5" r="4.5" />
      <path d="M10.5 12.5 20 3M17 6l2 2M14 9l2 2" />
    </svg>
  );
}
export function ListIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01" />
    </svg>
  );
}
export function PlayIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M6 4l14 8-14 8z" />
    </svg>
  );
}
export function ServerIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <rect x="3" y="4" width="18" height="7" rx="1.5" />
      <rect x="3" y="13" width="18" height="7" rx="1.5" />
      <path d="M7 7.5h.01M7 16.5h.01" />
    </svg>
  );
}
export function SettingsIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M4 6h10M18 6h2M4 12h2M10 12h10M4 18h6M14 18h6" />
      <circle cx="16" cy="6" r="2" />
      <circle cx="8" cy="12" r="2" />
      <circle cx="12" cy="18" r="2" />
    </svg>
  );
}
export function CopyIcon({ size = 14 }) {
  return (
    <svg {...base(size)}>
      <rect x="9" y="9" width="11" height="11" rx="2" />
      <path d="M5 15V5a2 2 0 0 1 2-2h8" />
    </svg>
  );
}
export function CheckIcon({ size = 14 }) {
  return (
    <svg {...base(size)} stroke="var(--color-success)">
      <path d="M20 6 9 17l-5-5" />
    </svg>
  );
}
export function TrashIcon({ size = 16 }) {
  return (
    <svg {...base(size)}>
      <path d="M4 7h16M9 7V5a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2M6 7l1 13a1 1 0 0 0 1 1h8a1 1 0 0 0 1-1l1-13" />
    </svg>
  );
}
export function EyeIcon({ size = 16 }) {
  return (
    <svg {...base(size)}>
      <path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7z" />
      <circle cx="12" cy="12" r="3" />
    </svg>
  );
}
export function EyeOffIcon({ size = 16 }) {
  return (
    <svg {...base(size)}>
      <path d="M3 3l18 18M10.6 10.6a3 3 0 0 0 4.2 4.2M9.9 5.2A9.6 9.6 0 0 1 12 5c6.5 0 10 7 10 7a17 17 0 0 1-3.2 4M6.1 6.1A17 17 0 0 0 2 12s3.5 7 10 7a9.5 9.5 0 0 0 2.1-.2" />
    </svg>
  );
}
export function CodeIcon({ size = 16 }) {
  return (
    <svg {...base(size)}>
      <path d="M8 8l-4 4 4 4M16 8l4 4-4 4" />
    </svg>
  );
}
export function RefreshIcon({ size = 14 }) {
  return (
    <svg {...base(size)}>
      <path d="M4 12a8 8 0 0 1 13.5-5.8L21 9M20 12a8 8 0 0 1-13.5 5.8L3 15" />
      <path d="M21 4v5h-5M3 20v-5h5" />
    </svg>
  );
}
export function ChevronLeftIcon({ size = 14 }) {
  return (
    <svg {...base(size)} className="icon-mirror">
      <path d="M15 6l-6 6 6 6" />
    </svg>
  );
}
export function ChevronRightIcon({ size = 14 }) {
  return (
    <svg {...base(size)} className="icon-mirror">
      <path d="M9 6l6 6-6 6" />
    </svg>
  );
}
export function ChevronsLeftIcon({ size = 14 }) {
  return (
    <svg {...base(size)} className="icon-mirror">
      <path d="M18 6l-6 6 6 6M12 6l-6 6 6 6" />
    </svg>
  );
}
export function ChevronsRightIcon({ size = 14 }) {
  return (
    <svg {...base(size)} className="icon-mirror">
      <path d="M6 6l6 6-6 6M12 6l6 6-6 6" />
    </svg>
  );
}
export function ChevronDownIcon({ size = 14 }) {
  return (
    <svg {...base(size)}>
      <path d="M6 9l6 6 6-6" />
    </svg>
  );
}
export function MoreVerticalIcon({ size = 16 }) {
  return (
    <svg {...base(size)}>
      <circle cx="12" cy="5" r="1.4" />
      <circle cx="12" cy="12" r="1.4" />
      <circle cx="12" cy="19" r="1.4" />
    </svg>
  );
}
export function MenuIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M4 6h16M4 12h16M4 18h16" />
    </svg>
  );
}
export function SunIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2v2M12 20v2M4 12H2M22 12h-2M5 5l1.5 1.5M17.5 17.5 19 19M19 5l-1.5 1.5M6.5 17.5 5 19" />
    </svg>
  );
}
export function MoonIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M21 12.8A8.5 8.5 0 1 1 11.2 3a6.5 6.5 0 0 0 9.8 9.8z" />
    </svg>
  );
}
export function LogoutIcon({ size = 18 }) {
  return (
    <svg {...base(size)} className="icon-mirror">
      <path d="M15 4h3a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-3" />
      <path d="M10 17l-5-5 5-5M5 12h11" />
    </svg>
  );
}
export function GithubIcon({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="currentColor">
      <path d="M12 2C6.48 2 2 6.58 2 12.25c0 4.53 2.87 8.37 6.84 9.73.5.1.68-.22.68-.49 0-.24-.01-.87-.01-1.71-2.78.62-3.37-1.22-3.37-1.22-.46-1.18-1.11-1.5-1.11-1.5-.9-.63.07-.62.07-.62 1 .07 1.53 1.05 1.53 1.05.89 1.56 2.34 1.11 2.91.85.09-.66.35-1.11.63-1.37-2.22-.26-4.55-1.14-4.55-5.06 0-1.12.39-2.03 1.03-2.75-.1-.26-.45-1.3.1-2.7 0 0 .84-.28 2.75 1.05a9.3 9.3 0 0 1 5 0c1.91-1.33 2.75-1.05 2.75-1.05.55 1.4.2 2.44.1 2.7.64.72 1.03 1.63 1.03 2.75 0 3.93-2.34 4.79-4.57 5.05.36.32.68.94.68 1.9 0 1.37-.01 2.48-.01 2.82 0 .27.18.6.69.49A10.02 10.02 0 0 0 22 12.25C22 6.58 17.52 2 12 2z" />
    </svg>
  );
}
export function DiscordIcon({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="currentColor">
      <path d="M20.32 4.94A19.6 19.6 0 0 0 15.45 3.4a.07.07 0 0 0-.08.04c-.21.38-.44.87-.61 1.26a18.1 18.1 0 0 0-5.42 0 12.2 12.2 0 0 0-.62-1.26.08.08 0 0 0-.08-.04A19.5 19.5 0 0 0 3.68 4.94a.07.07 0 0 0-.03.03C.55 9.6-.3 14.15.12 18.63a.08.08 0 0 0 .03.06 19.7 19.7 0 0 0 5.94 3 .08.08 0 0 0 .09-.03c.46-.62.86-1.28 1.21-1.97a.08.08 0 0 0-.04-.11c-.64-.24-1.25-.54-1.84-.87a.08.08 0 0 1-.01-.13c.12-.09.25-.19.36-.28a.07.07 0 0 1 .08-.01c3.86 1.76 8.03 1.76 11.85 0a.07.07 0 0 1 .08.01c.12.1.24.19.37.29a.08.08 0 0 1-.01.13c-.59.34-1.2.63-1.84.87a.08.08 0 0 0-.04.11c.36.69.76 1.35 1.2 1.97a.08.08 0 0 0 .09.03 19.6 19.6 0 0 0 5.95-3 .08.08 0 0 0 .03-.06c.5-5.18-.84-9.69-3.54-13.66a.06.06 0 0 0-.03-.03zM8.02 15.9c-1.18 0-2.15-1.08-2.15-2.41 0-1.33.95-2.42 2.15-2.42 1.21 0 2.17 1.1 2.15 2.42 0 1.33-.95 2.41-2.15 2.41zm7.97 0c-1.18 0-2.15-1.08-2.15-2.41 0-1.33.95-2.42 2.15-2.42 1.21 0 2.17 1.1 2.15 2.42 0 1.33-.94 2.41-2.15 2.41z" />
    </svg>
  );
}
export function XIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M6 6l12 12M18 6 6 18" />
    </svg>
  );
}
export function PlusIcon({ size = 16 }) {
  return (
    <svg {...base(size)}>
      <path d="M12 5v14M5 12h14" />
    </svg>
  );
}
export function ColumnsIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <rect x="3" y="4" width="18" height="16" rx="2" /><path d="M9 4v16M15 4v16" />
    </svg>
  );
}

export function SendIcon({ size = 18 }) {
  return (
    <svg {...base(size)} className="icon-mirror">
      <path d="M22 2 11 13" /><path d="M22 2 15 22l-4-9-9-4 20-7z" />
    </svg>
  );
}

export function RunsIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M4 6h16M4 12h10M4 18h7" /><circle cx="18" cy="16" r="3" />
    </svg>
  );
}

export function QueueIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="m12 3 9 5-9 5-9-5 9-5z" /><path d="m3 13 9 5 9-5" />
    </svg>
  );
}

export function CpuIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <rect x="5" y="5" width="14" height="14" rx="2" /><rect x="9" y="9" width="6" height="6" /><path d="M9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3" />
    </svg>
  );
}

export function BotIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <rect x="4" y="8" width="16" height="12" rx="3" /><path d="M12 4v4M9 13h.01M15 13h.01M9 17h6" />
    </svg>
  );
}

export function BoxIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="m21 8-9-5-9 5v8l9 5 9-5V8z" /><path d="m3 8 9 5 9-5M12 13v8" />
    </svg>
  );
}

export function TerminalIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="m4 17 6-5-6-5M12 19h8" />
    </svg>
  );
}

export function FileTextIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z" /><path d="M14 3v6h6M8 13h8M8 17h6" />
    </svg>
  );
}

export function PlugIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M9 2v6M15 2v6M7 8h10v4a5 5 0 0 1-10 0V8zM12 17v5" />
    </svg>
  );
}

export function VaultIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <rect x="3" y="4" width="18" height="16" rx="2" /><circle cx="12" cy="12" r="3" /><path d="M12 9V7M12 17v-2" />
    </svg>
  );
}

export function FolderIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7z" />
    </svg>
  );
}

export function FileCodeIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z" /><path d="m10 13-2 2 2 2M14 13l2 2-2 2" />
    </svg>
  );
}

export function ShieldIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M12 3 4 6v6c0 5 3.5 8 8 9 4.5-1 8-4 8-9V6l-8-3z" />
    </svg>
  );
}

export function ShieldCheckIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M12 3 4 6v6c0 5 3.5 8 8 9 4.5-1 8-4 8-9V6l-8-3z" /><path d="m9 12 2 2 4-4" />
    </svg>
  );
}

export function LinkIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M10 14a5 5 0 0 0 7 0l3-3a5 5 0 0 0-7-7l-1 1" /><path d="M14 10a5 5 0 0 0-7 0l-3 3a5 5 0 0 0 7 7l1-1" />
    </svg>
  );
}

export function WandIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="m15 4 5 5L9 20H4v-5L15 4z" /><path d="M13 6l5 5" />
    </svg>
  );
}

export function HelpIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <circle cx="12" cy="12" r="9" /><path d="M9.5 9a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .8-1 1.5V14M12 17h.01" />
    </svg>
  );
}

export function StopIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <rect x="6" y="6" width="12" height="12" rx="1" />
    </svg>
  );
}

export function UploadIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M12 16V4M7 9l5-5 5 5M4 20h16" />
    </svg>
  );
}

export function DownloadIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M12 4v12M7 11l5 5 5-5M4 20h16" />
    </svg>
  );
}

export function RetryIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M3 12a9 9 0 1 0 3-6.7" /><path d="M3 4v5h5" />
    </svg>
  );
}

export function TagIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M3 12V4h8l10 10-8 8L3 12z" /><circle cx="7.5" cy="7.5" r="1.5" />
    </svg>
  );
}

export function SlidersIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12M20 18h0" /><circle cx="16" cy="6" r="2" /><circle cx="10" cy="12" r="2" /><circle cx="18" cy="18" r="2" />
    </svg>
  );
}

export function ChartIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M4 20V10M10 20V4M16 20v-7M22 20H2" />
    </svg>
  );
}

export function PoolIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <circle cx="7" cy="7" r="3" /><circle cx="17" cy="7" r="3" /><circle cx="7" cy="17" r="3" /><circle cx="17" cy="17" r="3" />
    </svg>
  );
}
export function InfoIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 11v6M12 7.5h.01" />
    </svg>
  );
}

export function ChatIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M21 12a8 8 0 0 1-11.5 7.2L4 21l1.8-5.2A8 8 0 1 1 21 12z" />
    </svg>
  );
}

export function MessagesIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M14 9a2 2 0 0 1-2 2H7l-4 3V5a2 2 0 0 1 2-2h7a2 2 0 0 1 2 2z" />
      <path d="M18 9h1a2 2 0 0 1 2 2v10l-4-3h-5a2 2 0 0 1-2-2v-1" />
    </svg>
  );
}

export function BookIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20V3H6.5A2.5 2.5 0 0 0 4 5.5z" />
      <path d="M4 19.5A2.5 2.5 0 0 0 6.5 22H20v-5" />
    </svg>
  );
}

export function PencilIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M17 3a2.8 2.8 0 0 1 4 4L7.5 20.5 2 22l1.5-5.5z" />
    </svg>
  );
}

export function AlertTriangleIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z" />
      <path d="M12 9v4M12 17h.01" />
    </svg>
  );
}

export function DatabaseIcon({ size = 18 }) {
  return (
    <svg {...base(size)}>
      <ellipse cx="12" cy="5" rx="8" ry="3" />
      <path d="M4 5v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5" />
      <path d="M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3" />
    </svg>
  );
}
