# Design System Specification: The Tactile Ledger

## 1. Overview & Creative North Star
**Creative North Star: "Financial Serenity"**
Traditional accounting software often feels cluttered, rigid, and anxiety-inducing—defined by a labyrinth of harsh borders and clinical grey scales. This design system rejects the "grid-of-boxes" mentality in favor of an **Editorial Tonalism**. By removing lines and embracing "No-Line" architecture, we create a UI that feels like high-end stationery: tactile, expansive, and authoritative.

The goal is to move beyond a "template" look. We utilize intentional asymmetry, significant whitespace, and "The Tactile Ledger" philosophy—where depth is achieved through the physical stacking of warm, paper-like surfaces rather than digital strokes.

## 2. Colors & Surface Architecture
The palette is rooted in warmth and prestige, utilizing a sophisticated range of parchment and gold tones to establish trust and legibility.

### The "No-Line" Rule
**Strict Mandate:** 1px solid borders are prohibited for sectioning or containment. 
Boundaries must be defined solely through background color shifts. For example, a `surface-container-low` element sitting on a `surface` background creates a natural edge that the human eye perceives as a boundary without the "noise" of a line.

### Surface Hierarchy & Nesting
Treat the UI as a series of physical layers. Use the following tiers to create depth:
*   **Base Layer (`surface` / `#fbfaee`):** The primary canvas.
*   **Content Layer (`surface-container-low` / `#f5f4e8`):** The standard background for the `.ledger-card`.
*   **Focus Layer (`surface-container-lowest` / `#ffffff`):** Reserved for active input fields or highlighted data points to make them "pop" against the warmer background.
*   **Elevated Layer (`surface-container-high` / `#eae9dd`):** Used for persistent sidebars or navigation elements that sit "underneath" the main content flow.

### The Gold Gradient
The primary accent is not a flat color but a **Signature Gradient**. This provides a "visual soul" that signifies premium quality.
*   **Primary Gradient:** Linear-gradient(135deg, `#785600` 0%, `#5a4000` 100%)
*   **Use Case:** Main Action Buttons (CTAs), Hero Data Points, and Active State indicators.

## 3. Typography: The Manrope Scale
We use **Manrope** for its geometric clarity and exceptional numerical legibility—a requirement for accounting.

### Numerical Authority
All amounts and dates must use `font-variant-numeric: tabular-nums` to ensure columns of figures align perfectly. We use `clamp()` for fluid, responsive sizing:
*   **Hero Amounts:** `font-size: clamp(2.25rem, 5vw, 3.5rem);` (Display-LG)
*   **Section Headers:** `font-size: clamp(1.5rem, 3vw, 2rem);` (Headline-LG)

### Hierarchy Levels
*   **Display (LG/MD):** For high-level portfolio totals. High tracking (letter-spacing: -0.02em).
*   **Title (LG/MD):** For card titles. Semibold weight to replace the need for bold separators.
*   **Label (MD/SM):** For metadata. Uppercase with increased letter-spacing (+0.05em) to provide an editorial "tag" feel.

## 4. Elevation & Depth
In a system without lines, depth is our primary communication tool.

### The Layering Principle
Depth is achieved by stacking surface-container tiers. Placing a `#ffffff` (surface-container-lowest) card on a `#f5f4e8` (surface-container-low) section creates a soft, natural lift.

### Ambient Shadows
For floating elements (modals, dropdowns), avoid standard grey shadows. 
*   **Shadow Definition:** `box-shadow: 0 20px 40px rgba(48, 49, 41, 0.06);`
*   **Note:** Use a tinted version of `on-surface` (`#303129`) at ultra-low opacity to mimic natural light falling on parchment.

### Glassmorphism & Depth
For overlays and navigation bars, use **Backdrop Blur**.
*   **Value:** `background: rgba(251, 250, 238, 0.8); backdrop-filter: blur(12px);`
This allows the "warmth" of the underlying data to bleed through, maintaining a cohesive atmosphere even when modals are open.

## 5. Components

### The Ledger Card (`.ledger-card`)
The fundamental unit of the system.
*   **Style:** No border. Background: `surface-container-low` (`#f5f4e8`). 
*   **Radius:** `0.375rem` (md).
*   **Spacing:** Generous internal padding (minimum 2rem) to allow data to breathe. 
*   **Separation:** Content within the card is separated by `2rem` of vertical whitespace rather than divider lines.

### Gold Gradient Buttons
*   **Primary:** `background: linear-gradient(135deg, #785600, #5a4000); color: #ffffff;`
*   **Shape:** `0.25rem` (default) radius for a professional, sharp-but-approachable look.
*   **Hover State:** Increase the gradient saturation or shift the angle; do not use a "darken" overlay.

### Input Fields
*   **Style:** Inset tonal shift. 
*   **Background:** `surface-container-lowest` (`#ffffff`).
*   **State:** On focus, use a subtle 2px glow of `surface-tint` at 20% opacity. No harsh outlines.

### Lists & Data Grids
*   **Structure:** Forbid divider lines. Use alternating row colors (Zebra striping) with a delta of only 2%—e.g., alternating between `surface-container-low` and `surface-container-high`.
*   **Typography:** All currency figures should be right-aligned using `title-md`.

## 6. Do's and Don'ts

### Do:
*   **DO** use whitespace as a structural element. If a section feels messy, add space instead of a line.
*   **DO** use `Manrope` for all financial figures. It is the "voice" of the data.
*   **DO** use subtle transitions (200ms ease-out) for all tonal shifts to reinforce the premium feel.

### Don't:
*   **DON'T** use 100% black (`#000000`) for text. Use `on-surface` (`#1b1c15`) to maintain the warm, high-end editorial feel.
*   **DON'T** ever use a standard 1px `#ccc` border. If separation is failing, reconsider your background tonal hierarchy.
*   **DON'T** use default "Material Blue" or "Success Green." Use the `tertiary` and `error` tokens provided to ensure they harmonize with the gold and cream palette.

### Accessibility Note:
While we avoid lines, we must maintain contrast. Ensure that the contrast ratio between `surface` and `on-surface` text remains at least 7:1 for all primary data. The "No-Line" rule applies to structural sectioning, not to functional iconography which requires high visibility.