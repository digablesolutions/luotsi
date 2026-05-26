namespace Luotsi.Cli.Artifacts;

internal static class ArtifactIndexTheme
{
    public const string Css = """
    :root {
      color-scheme: dark;
      --bg: #07111f;
      --surface: #0f172a;
      --panel: #111827;
      --panel-strong: #0b1220;
      --panel-subtle: #111c2e;
      --rail: #050b14;
      --text: #f8fafc;
      --muted: #cbd5e1;
      --muted-weak: #94a3b8;
      --line: #26364a;
      --line-soft: #1f2d3f;
      --accent: #38bdf8;
      --accent-strong: #7dd3fc;
      --accent-soft: rgba(56, 189, 248, .11);
      --warning: #fbbf24;
      --danger: #fb7185;
      --danger-soft: rgba(251, 113, 133, .11);
      --success: #34d399;
      --shadow: 0 22px 64px rgba(1, 8, 19, .34);
      --code-bg: #020617;
    }

    * { box-sizing: border-box; }
    body { margin: 0; font: 14px/1.45 Inter, ui-sans-serif, system-ui, -apple-system, Segoe UI, sans-serif; background: radial-gradient(circle at top left, rgba(56,189,248,.08), transparent 36rem), var(--bg); color: var(--text); }
    body.has-workbench { padding-left: 74px; }
    .app-rail { position: fixed; inset: 0 auto 0 0; z-index: 3; width: 74px; border-right: 1px solid #020617; background: var(--rail); display: grid; grid-template-rows: auto 1fr auto; padding: 10px 10px 12px; }
    .rail-brand { position: relative; display: grid; place-items: center; width: 34px; height: 34px; margin: 0 auto 16px; border: 1px solid rgba(125,211,252,.45); border-radius: 8px; background: #0f172a; box-shadow: inset 0 0 0 1px rgba(248,250,252,.06), 0 8px 22px rgba(0,0,0,.3); }
    .rail-brand::before { content: ""; width: 21px; height: 21px; border: 2px solid #e2e8f0; border-radius: 999px; }
    .rail-brand::after { content: ""; position: absolute; width: 0; height: 0; border-left: 5px solid transparent; border-right: 5px solid transparent; border-bottom: 16px solid var(--accent); transform: translateY(-3px); }
    .rail-nav { display: grid; gap: 9px; align-content: start; }
    .rail-link { display: grid; place-items: center; width: 46px; min-height: 46px; margin: 0 auto; border: 1px solid transparent; border-radius: 8px; color: var(--muted); font-size: 11px; font-weight: 680; text-decoration: none; }
    .rail-link:hover, .rail-link.active { border-color: rgba(56,189,248,.56); background: var(--accent-soft); color: var(--text); text-decoration: none; }
    main { position: relative; max-width: none; margin: 0; padding: 18px 22px 56px; }
    header { margin: 0 0 12px; padding: 16px 18px; border: 1px solid var(--line); background: linear-gradient(180deg, rgba(15,23,42,.98), rgba(15,23,42,.92)); border-radius: 8px; box-shadow: var(--shadow); }
    .eyebrow { margin: 0 0 8px; color: var(--accent-strong); font-size: 11px; font-weight: 760; letter-spacing: .08em; text-transform: uppercase; }
    .workbench-header { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 18px; align-items: start; }
    .breadcrumbs { margin-bottom: 10px; color: var(--muted); font-size: 13px; }
    h1 { margin: 0 0 8px; font-size: 32px; line-height: 1.08; letter-spacing: 0; }
    .workbench-subtitle { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; color: var(--muted); }
    .headline-metrics { display: grid; grid-auto-flow: column; gap: 20px; text-align: right; }
    .headline-metric span { display: block; color: var(--muted); font-size: 11px; font-weight: 720; text-decoration: underline; text-decoration-style: dotted; text-underline-offset: 3px; }
    .headline-metric strong { display: block; margin-top: 2px; font-size: 22px; line-height: 1; }
    .root { color: var(--muted); word-break: break-word; overflow-wrap: anywhere; }
    .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: 8px; margin-top: 14px; }
    .stat { padding: 11px 13px; border: 1px solid var(--line-soft); border-radius: 7px; background: var(--panel-subtle); }
    .stat-value { display: block; font-size: 24px; font-weight: 760; line-height: 1.1; }
    .stat-label { display: block; margin-top: 4px; color: var(--muted); font-size: 11px; text-transform: uppercase; letter-spacing: .06em; }
    section { margin-top: 12px; border: 1px solid var(--line); background: var(--panel); border-radius: 8px; overflow: hidden; box-shadow: 0 8px 24px rgba(1,8,19,.18); }
    h2 { margin: 0; padding: 12px 14px; font-size: 14px; line-height: 1.2; border-bottom: 1px solid var(--line); background: var(--panel-strong); letter-spacing: 0; }
    ul { list-style: none; margin: 0; padding: 0; }
    li { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 16px; align-items: start; padding: 13px 16px; border-top: 1px solid var(--line-soft); }
    li:first-child { border-top: 0; }
    a { color: var(--accent-strong); text-decoration: none; overflow-wrap: anywhere; }
    a:hover { text-decoration: underline; }
    code { display: inline-block; max-width: 100%; padding: 4px 7px; border: 1px solid var(--line); border-radius: 6px; background: var(--code-bg); color: var(--text); font: 12px/1.45 ui-monospace, SFMono-Regular, Consolas, Liberation Mono, monospace; overflow-wrap: anywhere; }
    .timeline-label { margin-top: 12px; color: var(--muted); font-size: 12px; font-weight: 700; text-transform: uppercase; letter-spacing: .06em; }
    .timeline { list-style: none; margin: 8px 0 0; padding: 0; border-left: 1px solid var(--line); }
    .timeline li { display: block; position: relative; padding: 7px 0 0 14px; border-top: 0; color: var(--muted); font-family: ui-monospace, SFMono-Regular, Consolas, Liberation Mono, monospace; font-size: 12px; }
    .timeline li::before { content: ""; position: absolute; left: -4px; top: 15px; width: 7px; height: 7px; border-radius: 999px; background: var(--accent); box-shadow: 0 0 0 3px var(--panel); }
    .timeline li.timeline-failure { color: var(--text); }
    .timeline li.timeline-failure::before { background: var(--danger); box-shadow: 0 0 0 3px var(--panel), 0 0 0 6px rgba(251,113,133,.12); }
    .timeline-type { display: inline-block; margin-right: 6px; color: var(--accent-strong); font-weight: 760; }
    .timeline-tags { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; border: 0; }
    .kind { color: var(--muted); font-size: 10px; font-weight: 760; text-transform: uppercase; letter-spacing: .08em; }
    .badge { min-width: 74px; justify-self: end; padding: 4px 8px; border: 1px solid var(--line); border-radius: 999px; background: color-mix(in srgb, var(--panel-strong) 78%, transparent); text-align: center; }
    .workflow ul { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 12px; padding: 14px; }
    .workflow li { display: block; min-height: 128px; padding: 15px; border: 1px solid var(--line-soft); border-radius: 8px; background: var(--surface); }
    .workflow li:first-child { border-top: 1px solid var(--line); }
    .workflow .kind { display: inline-block; margin-bottom: 10px; color: var(--accent); }
    .workflow code { margin-bottom: 9px; }
    .toolbar { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 12px; align-items: center; margin: 18px 0 0; }
    .search { width: 100%; min-height: 40px; padding: 9px 12px; border: 1px solid var(--line); border-radius: 8px; background: var(--panel-subtle); color: var(--text); outline: none; }
    .search:focus { border-color: var(--accent); box-shadow: 0 0 0 3px rgba(56,189,248,.2); }
    .jump-links { display: flex; gap: 8px; flex-wrap: wrap; justify-content: flex-end; }
    .jump-links a, .copy-command { display: inline-flex; align-items: center; min-height: 34px; padding: 7px 10px; border: 1px solid var(--line); border-radius: 8px; background: var(--panel-strong); color: var(--text); font: inherit; cursor: pointer; text-decoration: none; }
    .jump-links a:hover, .copy-command:hover { border-color: var(--accent); background: color-mix(in srgb, var(--accent) 10%, var(--panel-strong)); text-decoration: none; }
    .workbench { border-color: rgba(251,113,133,.38); }
    .workbench h2 { color: var(--text); }
    .workbench-layout { display: grid; grid-template-columns: minmax(0, 1fr) 310px; gap: 0; }
    .workbench-main { display: grid; grid-template-columns: minmax(0, 1fr) minmax(300px, .72fr); gap: 14px; padding: 14px; }
    .workbench-side { display: grid; gap: 14px; align-content: start; padding: 14px; border-left: 1px solid var(--line); background: color-mix(in srgb, var(--panel-strong) 62%, var(--panel)); }
    .panel { padding: 15px; border: 1px solid var(--line-soft); border-radius: 8px; background: var(--surface); }
    .hero-panel { display: grid; align-content: start; min-height: 100%; border-color: rgba(251,113,133,.42); background: linear-gradient(180deg, var(--surface), color-mix(in srgb, var(--danger) 5%, var(--surface))); }
    .panel h3 { margin: 0 0 10px; font-size: 12px; line-height: 1.25; letter-spacing: .06em; text-transform: uppercase; color: var(--muted); }
    .failure-title { margin: 0 0 8px; font-size: 22px; font-weight: 740; line-height: 1.2; letter-spacing: 0; }
    .failure-message { margin: 10px 0 0; padding: 10px 12px; border: 1px solid rgba(251,113,133,.42); border-left-width: 3px; border-radius: 6px; background: var(--danger-soft); overflow-wrap: anywhere; }
    .failure-brief { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 8px; margin: 12px 0; }
    .brief-card { min-height: 92px; padding: 10px 11px; border: 1px solid var(--line-soft); border-radius: 8px; background: var(--panel-subtle); }
    .brief-card span { display: block; margin-bottom: 6px; color: var(--muted); font-size: 10px; font-weight: 760; letter-spacing: .08em; text-transform: uppercase; }
    .brief-card strong { display: block; color: var(--text); font-size: 12px; font-weight: 680; line-height: 1.35; overflow-wrap: anywhere; }
    .chip-row { display: flex; flex-wrap: wrap; gap: 8px; margin: 0 0 12px; }
    .chip { display: inline-flex; align-items: center; gap: 6px; min-height: 26px; padding: 3px 8px; border: 1px solid var(--line); border-radius: 999px; background: var(--panel-subtle); color: var(--muted); font-size: 12px; font-weight: 650; }
    .chip-danger { border-color: rgba(251,113,133,.58); color: var(--danger); }
    .chip-success { border-color: rgba(52,211,153,.48); color: var(--success); }
    .chip-warning { border-color: rgba(251,191,36,.5); color: var(--warning); }
    .meta-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(130px, 1fr)); gap: 8px; margin-top: 12px; }
    .meta { padding: 9px 10px; border: 1px solid var(--line-soft); border-radius: 7px; background: var(--panel-subtle); }
    .meta span { display: block; color: var(--muted); font-size: 11px; text-transform: uppercase; letter-spacing: .06em; }
    .meta strong { display: block; margin-top: 3px; font-weight: 680; overflow-wrap: anywhere; }
    .evidence-list { display: grid; gap: 8px; margin: 0; padding: 0; }
    .evidence-list li { display: block; padding: 9px 10px; border: 1px solid var(--line-soft); border-radius: 7px; background: var(--panel-subtle); }
    .evidence-list li.primary-evidence { border-color: rgba(56,189,248,.46); background: var(--accent-soft); }
    .evidence-groups { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 9px; margin: 0 0 12px; }
    .evidence-group { min-width: 0; padding: 10px; border: 1px solid var(--line-soft); border-radius: 8px; background: color-mix(in srgb, var(--panel-subtle) 84%, var(--panel-strong)); }
    .evidence-group-failure { border-color: rgba(251,113,133,.42); background: color-mix(in srgb, var(--danger-soft) 38%, var(--panel-subtle)); }
    .evidence-group-context { border-color: rgba(125,211,252,.28); }
    .evidence-group-action { border-color: rgba(251,191,36,.32); }
    .evidence-group-artifact { border-color: rgba(52,211,153,.3); }
    .evidence-group-header { display: grid; gap: 3px; margin-bottom: 7px; }
    .evidence-group-header strong { font-size: 13px; }
    .evidence-group-items { display: grid; gap: 6px; margin-top: 9px; }
    .evidence-group-items li { display: grid; grid-template-columns: minmax(74px, .38fr) minmax(0, 1fr); gap: 8px; padding: 7px 0 0; border: 0; border-top: 1px solid var(--line-soft); background: transparent; font-size: 12px; }
    .evidence-group-items span { color: var(--muted); font-weight: 680; overflow-wrap: anywhere; }
    .evidence-group-items strong { font-weight: 560; overflow-wrap: anywhere; }
    .filter-chips { display: flex; flex-wrap: wrap; gap: 7px; margin: 0 0 9px; }
    .filter-chip { min-height: 28px; padding: 5px 9px; border: 1px solid var(--line); border-radius: 999px; background: var(--panel-subtle); color: var(--muted); font: inherit; font-size: 12px; cursor: pointer; }
    .filter-chip:hover, .filter-chip.active { border-color: var(--accent); background: var(--accent-soft); color: var(--text); }
    .media-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(132px, 1fr)); gap: 9px; margin: 0 0 12px; }
    .media-tile { display: grid; gap: 6px; min-width: 0; padding: 8px; border: 1px solid var(--line-soft); border-radius: 8px; background: var(--panel-subtle); color: var(--text); text-decoration: none; }
    .media-tile:hover { border-color: rgba(56,189,248,.5); text-decoration: none; }
    .media-tile img, .media-tile video { display: block; width: 100%; aspect-ratio: 16 / 9; object-fit: cover; border: 1px solid var(--line-soft); border-radius: 6px; background: var(--code-bg); }
    .media-tile span { color: var(--muted); font-size: 10px; font-weight: 760; letter-spacing: .08em; text-transform: uppercase; }
    .media-tile strong { min-width: 0; font-size: 12px; overflow-wrap: anywhere; }
    .media-placeholder { display: grid; place-items: center; width: 100%; aspect-ratio: 16 / 9; border: 1px dashed var(--line); border-radius: 6px; color: var(--muted); background: var(--code-bg); }
    .media-empty { margin: 0 0 10px; padding: 10px; border: 1px dashed var(--line); border-radius: 8px; }
    .triage-path { display: grid; gap: 10px; }
    .triage-step { display: grid; grid-template-columns: 32px minmax(0, 1fr); gap: 10px; align-items: start; padding: 10px; border: 1px solid var(--line-soft); border-radius: 8px; background: var(--panel-subtle); }
    .step-number { display: grid; place-items: center; width: 28px; height: 28px; border: 1px solid rgba(56,189,248,.42); border-radius: 999px; background: var(--accent-soft); color: var(--accent); font-weight: 760; }
    .step-title { font-weight: 720; }
    .command-row { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 8px; align-items: start; margin-top: 8px; }
    .command-row code { display: block; width: 100%; white-space: nowrap; overflow-x: auto; overflow-y: hidden; }
    .command-row .copy-command { flex: 0 0 auto; }
    .next-action { margin-top: 12px; }
    .next-action code { margin-top: 7px; }
    .empty { padding: 18px 16px; color: var(--muted); }
    @media (max-width: 1100px) { .workbench-layout { grid-template-columns: 1fr; } .workbench-side { border-left: 0; border-top: 1px solid var(--line); } }
    @media (max-width: 900px) { body.has-workbench { padding-left: 0; } .app-rail { display: none; } .workbench-main { grid-template-columns: 1fr; } .toolbar { grid-template-columns: 1fr; } .jump-links { justify-content: flex-start; } .workbench-header { grid-template-columns: 1fr; } .headline-metrics { grid-auto-flow: row; grid-template-columns: repeat(3, minmax(80px, 1fr)); text-align: left; } .failure-brief { grid-template-columns: 1fr; } }
    @media (max-width: 680px) { main { padding: 12px 10px 34px; } header { padding: 14px; } h1 { font-size: 26px; } li { grid-template-columns: 1fr; } .badge { justify-self: start; } .workflow ul { grid-template-columns: 1fr; } .command-row { grid-template-columns: 1fr; } }
    """;
}
