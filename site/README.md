# iKeyd landing page

Static landing page for iKeyd. It intentionally has no frontend build step or runtime dependency.

## Local preview

```bash
python -m http.server 8000 --directory site
```

Then open `http://localhost:8000`.

## Design constraints

- real iKeyd DSL is the primary visual language
- no glow, glassmorphism, background grid, fake terminal, fake metrics, or decorative dashboard UI
- cyan is a sparse functional accent
- keyboard-state visuals demonstrate actual documented behavior
- mobile is a deliberate single-column layout
- `prefers-reduced-motion` is respected

The canonical application icon and combined logo are copied from `docs/assets/brand/ikeyd-icon.png` and `docs/assets/brand/ikeyd-logo.png` by the Pages workflow.
