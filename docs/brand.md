# iKeyd brand direction

## Brand idea

> **Your keyboard, defined in code.**

iKeyd is a tool for defining and managing keyboard behavior as code. The product itself — especially the DSL and the behavior it produces — is the visual identity.

The public name remains **iKeyd**. The naming line **“I Key'd / I keyed it my way.”** remains part of the project's identity, but marketing surfaces should lead with the product idea rather than a slogan.

## Official assets

The current PNG files are canonical. Use them as-is: do not redraw, recolor, distort, crop, add glow, or reinterpret their silhouette.

### Application icon

<p align="center">
  <img src="assets/brand/ikeyd-icon.png" alt="iKeyd official icon" width="180">
</p>

`ikeyd-icon.png` is the canonical application and brand icon. The Windows executable and tray icon use `src/iKeyd.App/Assets/ikeyd.ico` derived from the same artwork.

### Logo / wordmark

<p align="center">
  <img src="assets/brand/ikeyd-logo.png" alt="iKeyd official logo and wordmark" width="420">
</p>

`ikeyd-logo.png` is the canonical combined logo and wordmark.

## Visual direction

The design should feel quiet, precise, programmable, tool-like, and real.

Prefer:

- near-black surfaces that resemble a calm editor or system tool
- large, confident typography and generous whitespace
- real `.ikeyd` source
- real compiler/runtime concepts
- keyboard geometry, key coordinates, tap/hold, layers, and combos as structural motifs
- thin borders only when they clarify structure
- monospace as a brand element, not decorative “terminal” styling
- cyan only for functional state: active, selected, cursor, important DSL tokens, primary action

Avoid:

- glow and gradient atmosphere
- background grids used as decoration
- glassmorphism and floating panels
- fake terminals, fake output, fake metrics, fake JSON, or fake status badges
- generic six-card SaaS feature grids
- large rounded dashboard cards
- decorative arrows, particles, orbs, circuitry, or cyberpunk styling
- generic “powerful / flexible / seamless” copy

## Core palette

| Role | Value |
| --- | --- |
| Background | `#0A0A0A` |
| Surface | `#111111` |
| Elevated surface | `#151515` |
| Primary text | `#F2F2F2` |
| Secondary text | `#8A8A8A` |
| Border | `#262626` |
| Accent | `#2ED3D0` |

Accent should normally stay below roughly 10% of the visual field. It is a system color, not ambient lighting.

## Typography

- Heading: neutral / industrial system sans, not excessively heavy
- Body: high-legibility sans
- Code and labels: monospace

Code is a primary brand surface. Keep syntax highlighting restrained to a few functional colors. Do not add fake editor chrome, macOS traffic lights, or invented filenames.

## Product storytelling

Use one idea per section:

1. **Hero** — Your keyboard, defined in code. + one real DSL example.
2. **Define** — Show readable authoring syntax.
3. **Compile** — Show the real DSL → static JSON profile relationship.
4. **Run** — Show minimal keyboard state that demonstrates documented behavior.
5. **Why iKeyd** — Versionable, reviewable, shareable, understandable.
6. **Real configuration** — Show a believable excerpt from `config/hotkeySKG.ikeyd`.

The product should remain recognizable as iKeyd even when the logo is hidden because the visual language comes from keyboard behavior and the DSL.

## Copy direction

Primary:

- **Your keyboard, defined in code.**
- **Keyboard behavior should be explicit.**
- **Define behavior, not shortcuts.**
- **Readable in. Predictable out.**
- **Code becomes behavior.**
- **Keyboard behavior belongs in source control.**

Keep copy short, technical, specific, and understated.

## Website

The landing page lives in `site/` and is intentionally static. It uses the canonical DSL documented in `docs/ikeyd-dsl.md` and a real configuration excerpt from `config/hotkeySKG.ikeyd`.

The page is deployed by `.github/workflows/pages.yml` when GitHub Pages is enabled for the repository.

## Assets

Brand assets live in `docs/assets/brand/`.

- `ikeyd-icon.png` — canonical application / brand mark
- `ikeyd-logo.png` — canonical combined logo / wordmark
- `readme-hero.png` — legacy/supporting README artwork retained for compatibility but no longer the primary brand language
- `readme-features.png` — legacy/supporting README artwork retained for compatibility
- `readme-dsl.png` — legacy/supporting README artwork retained for compatibility
- `src/iKeyd.App/Assets/ikeyd.ico` — Windows executable and tray icon

The landing page should use the official logo and real product code rather than inventing additional logos or illustration systems. Future imagery should prefer real code and real behavior over illustration.
