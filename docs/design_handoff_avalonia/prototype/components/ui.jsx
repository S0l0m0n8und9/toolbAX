/* Shared icons + small UI primitives.
   Everything exported to window for cross-file use. */

const Icon = ({ name, size = 14, stroke = 1.5 }) => {
  const common = { width: size, height: size, viewBox: "0 0 24 24", fill: "none", stroke: "currentColor", strokeWidth: stroke, strokeLinecap: "round", strokeLinejoin: "round" };
  switch (name) {
    case "search":   return <svg {...common}><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>;
    case "filter":   return <svg {...common}><path d="M3 5h18M6 12h12M10 19h4"/></svg>;
    case "plus":     return <svg {...common}><path d="M12 5v14M5 12h14"/></svg>;
    case "x":        return <svg {...common}><path d="M6 6l12 12M18 6 6 18"/></svg>;
    case "chev-r":   return <svg {...common}><path d="m9 6 6 6-6 6"/></svg>;
    case "chev-d":   return <svg {...common}><path d="m6 9 6 6 6-6"/></svg>;
    case "play":     return <svg {...common}><path d="M6 4v16l14-8z"/></svg>;
    case "pause":    return <svg {...common}><path d="M6 5h4v14H6zM14 5h4v14h-4z"/></svg>;
    case "stop":     return <svg {...common}><rect x="6" y="6" width="12" height="12"/></svg>;
    case "refresh":  return <svg {...common}><path d="M3 12a9 9 0 0 1 15.5-6.3L21 8"/><path d="M21 3v5h-5"/><path d="M21 12a9 9 0 0 1-15.5 6.3L3 16"/><path d="M3 21v-5h5"/></svg>;
    case "download": return <svg {...common}><path d="M12 3v12M7 10l5 5 5-5M4 21h16"/></svg>;
    case "save":     return <svg {...common}><path d="M4 4h12l4 4v12H4zM8 4v6h8V4M8 20v-6h8v6"/></svg>;
    case "database": return <svg {...common}><ellipse cx="12" cy="5" rx="8" ry="3"/><path d="M4 5v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5"/><path d="M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"/></svg>;
    case "plug":     return <svg {...common}><path d="M9 2v6M15 2v6M7 8h10v4a5 5 0 0 1-10 0zM12 17v5"/></svg>;
    case "map":      return <svg {...common}><path d="M3 6v14l6-3 6 3 6-3V3l-6 3-6-3-6 3z"/><path d="M9 3v14M15 6v14"/></svg>;
    case "settings": return <svg {...common}><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.9.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.9 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1 1.7 1.7 0 0 0-.3-1.9l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.9.3h.1a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.9V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/></svg>;
    case "user":     return <svg {...common}><circle cx="12" cy="8" r="4"/><path d="M4 21a8 8 0 0 1 16 0"/></svg>;
    case "key":      return <svg {...common}><circle cx="8" cy="15" r="4"/><path d="m11 12 9-9M17 6l3 3"/></svg>;
    case "check":    return <svg {...common}><path d="m4 12 5 5L20 6"/></svg>;
    case "terminal": return <svg {...common}><path d="m4 7 5 5-5 5M12 17h8"/></svg>;
    case "logs":     return <svg {...common}><path d="M4 5h16M4 10h16M4 15h10M4 20h7"/></svg>;
    case "branch":   return <svg {...common}><circle cx="6" cy="5" r="2"/><circle cx="6" cy="19" r="2"/><circle cx="18" cy="7" r="2"/><path d="M6 7v10M18 9c0 5-6 3-6 10"/></svg>;
    case "history":  return <svg {...common}><path d="M3 12a9 9 0 1 0 3-6.7L3 8"/><path d="M3 3v5h5"/><path d="M12 7v5l3 2"/></svg>;
    case "arrow-lr": return <svg {...common}><path d="M4 12h16M8 7l-4 5 4 5M16 17l4-5-4-5"/></svg>;
    case "arrow-r":  return <svg {...common}><path d="M5 12h14M13 6l6 6-6 6"/></svg>;
    case "arrow-l":  return <svg {...common}><path d="M19 12H5M11 6l-6 6 6 6"/></svg>;
    case "book":     return <svg {...common}><path d="M4 4v16a2 2 0 0 1 2-2h14V2H6a2 2 0 0 0-2 2zM6 18h14"/></svg>;
    case "bolt":     return <svg {...common}><path d="M13 2 4 14h7l-1 8 9-12h-7z"/></svg>;
    case "alert":    return <svg {...common}><path d="M12 9v4M12 17h.01"/><circle cx="12" cy="12" r="10"/></svg>;
    case "dot":      return <svg {...common}><circle cx="12" cy="12" r="4" fill="currentColor" stroke="none"/></svg>;
    case "sparkles": return <svg {...common}><path d="M12 3v4M12 17v4M3 12h4M17 12h4M6 6l2 2M16 16l2 2M6 18l2-2M16 8l2-2"/></svg>;
    case "menu":     return <svg {...common}><path d="M3 6h18M3 12h18M3 18h18"/></svg>;
    default:         return <svg {...common}/>;
  }
};

const StatusDot = ({ kind = "neutral" }) => {
  const color = {
    ok: "var(--ok)",
    warn: "var(--warn)",
    err: "var(--err)",
    info: "var(--info)",
    amber: "var(--accent)",
    neutral: "var(--paper-3)",
  }[kind];
  return (
    <span style={{
      display: "inline-block", width: 7, height: 7, borderRadius: "50%",
      background: color,
      boxShadow: kind !== "neutral" ? `0 0 0 2px color-mix(in oklch, ${color} 20%, transparent)` : "none",
      flexShrink: 0,
    }}/>
  );
};

// Tiny SVG bar sparkline for Dual-Write activity
const Sparkline = ({ data, color = "var(--accent)", w = 80, h = 18 }) => {
  if (!data || data.length === 0) return null;
  const max = Math.max(...data, 1);
  const step = w / data.length;
  return (
    <svg width={w} height={h} style={{ display: "block" }}>
      {data.map((v, i) => {
        const bh = Math.max(1, (v / max) * (h - 2));
        return <rect key={i} x={i * step} y={h - bh} width={step - 1} height={bh} fill={color} opacity={0.7 + 0.3 * (v / max)}/>;
      })}
    </svg>
  );
};

// Simple 3-dot menu button
const MoreBtn = () => (
  <button className="btn ghost icon" aria-label="more">
    <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><circle cx="5" cy="12" r="1.6"/><circle cx="12" cy="12" r="1.6"/><circle cx="19" cy="12" r="1.6"/></svg>
  </button>
);

Object.assign(window, { Icon, StatusDot, Sparkline, MoreBtn });
