// Native hover tooltips. Same bubble as the game's tool tooltips (vanilla Tooltip resolved in
// vanilla.ts, or cs2/ui Tooltip) with the vanilla DescriptionTooltip title/description styling.
// Adapted from Grid Road Generator (Ti4goc). Note: a plain HTML title attribute shows nothing in
// Gameface, so every hint must go through <Tip>.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: one toolbar tooltip with a hotkey footer, using cs2/ui.
import React from "react";
import * as ui from "cs2/ui";
import styles from "../Panel.module.css";

/** What each setting does, shown when the pointer rests on its row. Keyed by the row's label. */
export const SETTING_TIPS: Record<string, string> = {
    "Style": "Contour suburb: streets along the hill, linked by gentle ramps. Terraced blocks: straighter, level streets, one terrace per block. Mountain lane: one winding road with hairpins and forest around it.",
    "Mix mode": "How a mix of zones is spread: Random picks for each lot, Clumps makes runs of neighbouring lots, Even spreads them out evenly.",
    "Steep hills": "Build on slopes up to 60 %, with up to 12 m of earthwork. Connectors may switch back to climb.",
    "Trees": "Forest on ground too steep to build, and trees on the banks and between lots.",
    "Road": "The road used for every new street.",
    "Block depth": "The distance between neighbouring streets along the hill. Deeper blocks leave room for deeper lots.",
    "Max road grade": "No new road is steeper than this.",
    "Steepest buildable": "Ground steeper than this gets no roads or lots and becomes forest.",
    "Lot width": "How wide each lot is, in zone cells of 8 m.",
    "Lot depth": "How deep each lot is, in zone cells of 8 m.",
    "Space between lots": "Empty cells between lots. Auto leaves planted gaps on steeper ground.",
    "Level the lots": "Grade each lot into a level pad before it is zoned. Off grades only the roads.",
    "Most earth": "The most ground a lot may cut or fill. A lot that needs more is left out.",
    "Steepest bank": "No slope the grading makes is steeper than this.",
    "Retaining walls": "Garden walls where a road runs through a cutting.",
    "Tree density": "How many trees to plant.",
    "Seed": "Which layout you get. Reroll picks another; the same seed and settings always give the same plan.",
};

/** A setting's description on hover, in the game's tooltip bubble; rows without one are left as they are. */
export function SettingTip({ label, children }: { label?: string; children: React.ReactElement }) {
    const text = label ? SETTING_TIPS[label] : undefined;
    if (!text || !ui.Tooltip) return children;
    return <ui.Tooltip direction="right" delayTime={400} hideOnInteraction tooltip={<div className={styles.tip}>
        <strong>{label}</strong>
        <div>{text}</div>
    </div>}>{children}</ui.Tooltip>;
}

export function ToolbarTip({ children }: { children: React.ReactElement }) {
    if (!ui.Tooltip) return children;
    return <ui.Tooltip direction="up" delayTime={250} tooltip={<div className={styles.tip}>
        <strong>Stepped Streets</strong>
        <div>Draw an area on a hill and plan a stepped neighbourhood.</div>
        <div className={styles.hotkey}>Ctrl+Shift+S</div>
    </div>}>{children}</ui.Tooltip>;
}
