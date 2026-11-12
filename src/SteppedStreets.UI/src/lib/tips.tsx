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
    "Roads": "Build new: lay out new streets and zone along them. Use existing: zone the empty cells along streets already in the area, with no new roads and no ground changes. Cells already zoned stay as they are.",
    "Junctions": "How cross streets meet the main streets. Crossroads: four-way junctions. Staggered: offset T-junctions, like brickwork. Traffic: one through road carries the traffic and is the only road that meets yours.",
    "Shape the ground": "Grade roads and lots into the ground. Off: no terrain changes at all; roads follow the ground as the game builds them and lots aren't levelled.",
    "Mix mode": "How a mix of zones is spread: Random picks for each lot, Clumps makes runs of neighbouring lots, Even spreads them out evenly.",
    "Steep hills": "Build on slopes up to 60 %, with up to 12 m of earthwork. Connectors may switch back to climb.",
    "Trees": "Forest on ground too steep to build, and trees on the banks and between lots.",
    "Greenery": "Trees, bushes and rocks on the empty land in the area once it is zoned. Never on roads, buildings or lots.",
    "Road": "The road used for every new street. Road Builder roads are listed too, if zoning is on for them.",
    "Main road": "The road for main streets and the links to your roads. Same as streets: one road for every street.",
    "Street trees": "The game's own grass strip or trees on the new streets, or on the streets you zone with Use existing.",
    "Footpaths": "Paths for people on foot through long blocks, from one street to the next.",
    "Pocket parks": "A stretch of frontage too short for a lot becomes a small park.",
    "Corner lots": "The zone for lots on a corner, for shops on the corners. From the mix: no special zone.",
    "Main street lots": "The zone for lots facing a main street, for shops or denser homes along it. From the mix: no special zone.",
    "Level lots": "Grade each lot into a level pad before it is zoned, as Build new does. Changes the ground of the streets you zone.",
    "Crosswalks": "Painted crossings where the new streets meet. Off: none, and people cross there less often. Your existing roads keep theirs.",
    "Block depth": "The distance between neighbouring streets along the hill. Deeper blocks leave room for deeper lots.",
    "Max road grade": "No new road is steeper than this.",
    "Steepest buildable": "Ground steeper than this gets no roads or lots and becomes forest.",
    "Lot width": "How wide each lot is, in zone cells of 8 m.",
    "Lot depth": "How deep each lot is, in zone cells of 8 m. Only this depth is levelled and zoned. Low-density houses are 2 or 4 cells deep, so 4 fits them all.",
    "Space between lots": "Empty cells between lots. Auto leaves planted gaps on steeper ground.",
    "Level the lots": "Grade each lot into a level pad before it is zoned. Off grades only the roads.",
    "Most earth": "The most ground a lot may cut or fill. A lot that needs more is left out.",
    "Steepest bank": "No slope the grading makes is steeper than this.",
    "Retaining walls": "Garden walls where a road runs through a cutting.",
    "Tree density": "How many trees to plant.",
    "Seed": "Which layout you get. Reroll picks another; the same seed and settings always give the same plan.",
    "Preset": "Puts a saved set of settings back and plans the area again. The cross beside a preset deletes it.",
    "Save as": "Saves every setting under this name, to load again in any city. The same name replaces that preset.",
    "Remove": "Removes what the last build placed: its roads, zoning, greenery and buildings, and houses grown on its lots. The ground changes stay. Only until the next build, or until you load a city or quit.",
    "Report a bug": "Opens a new issue on GitHub. Add a screenshot and your SteppedStreets.log.",
    "Discord": "Questions, ideas and news.",
};

/** What each layout makes, shown under the layout picker. */
export const LAYOUT_TIPS: Record<string, string> = {
    ContourSuburb: "Streets along the hill, linked by gentle ramps; every lot on its own level pad.",
    TerracedBlocks: "Straighter, level streets along the hill, one terrace per block.",
    MountainLane: "One winding road climbing the hill with hairpins, forest around it.",
    SquareGrid: "Straight streets at right angles; blocks as long as they are deep. For flat or gentle ground.",
    RectangularGrid: "Straight streets at right angles with long blocks. For flat or gentle ground.",
    IrregularGrid: "A grid with varied street spacing and block lengths, less regimented.",
    SkewedGrid: "Long blocks whose cross streets lean, so the blocks are parallelograms.",
    FragmentedGrid: "Parallel streets with gaps: some cross streets left out and some streets broken.",
    DiagonalGrid: "A rectangular grid crossed by two diagonal avenues.",
    CurvedGrid: "A grid whose streets curve in gentle waves.",
    WarpedGrid: "Curving parallel streets with gaps, like a fragmented grid that bends.",
    RadialGrid: "A grid with four diagonal avenues radiating from a square at its centre.",
    Star: "Streets radiating from a small central ring, twice as many past a boulevard ring.",
    Rings: "Concentric ring streets joined by staggered radial streets.",
    Spiderweb: "Concentric rings crossed by radial streets running straight out from the centre.",
    Fan: "Arcs and radial streets opening like a fan from the edge nearest your road.",
    Spine: "A trunk street from your road with straight side streets off both sides.",
    Ladder: "Rails running in from your road, joined by rungs one block apart.",
    Comb: "A trunk along one side with side streets off one side only, like a comb.",
    Fishbone: "A trunk with side streets slanting off it in pairs, like the bones of a fish.",
    Tree: "A trunk with limbs, and twigs off the limbs: streets branching like a tree.",
    CulDeSacs: "Trunks from your road with short dead-end streets off both sides.",
    Loops: "Trunks with loop streets off both sides, each leaving and rejoining its trunk.",
    LoopsAndLollipops: "Trunks with loops and lollipops in turn.",
    Lollipops: "Trunks with short sticks off both sides, each ending in a small ring street.",
    TriangularMesh: "Streets in three directions 60° apart, so every block is a triangle.",
    Hexagonal: "A honeycomb of streets: hexagonal blocks, every junction a three-way one.",
    HexagonalSpokes: "Larger hexagons, each split into three diamonds by streets from its centre.",
    Ridge: "A main road along the crest of the hill, contour streets down both sides.",
    Valley: "A main road along the valley floor (one on each bank of a river), contour streets up both sides.",
    Coastal: "Streets parallel to the shore, joined by streets down to the water.",
    Winding: "One road winding back and forth across the area, with a cross street every few blocks.",
    OrganicMesh: "Streets that wave their own ways and cross at varied angles: every block different.",
    Hybrid: "A grid on one side of the area, loops and lollipops on the other.",
    Polycentric: "Two to four small centres, each its own neighbourhood of rings and radial streets.",
    Superblock: "Big blocks ringed by through streets, with only dead-end streets inside.",
    Radburn: "Superblocks of short cul-de-sacs round a green space in the middle of each.",
    FusedGrid: "A grid of through streets with loops inside each big block.",
    FilteredGrid: "A grid whose streets close in the middle of every other block: no through traffic.",
    BarcelonaSuperblock: "Three-by-three superblocks whose inner streets turn back to the edge round a central square.",
};

/** What each number in the plan and result means. */
export const STAT_TIPS: Record<string, string> = {
    "Roads": "Length of the new roads.",
    "Lots": "Lots to zone. Each grows a house or shop once the city has demand for its zone.",
    "Zoned cells": "Zone cells the lots cover, each 8 m square.",
    "Cells to zone": "Empty zone cells along the streets that get your zone mix, each 8 m square.",
    "Earth moved": "Cut and fill to level the roads and lots.",
    "Steepest road": "The steepest grade on any new road.",
    "Forest": "Share of the area too steep to build on, left as forest.",
    "Connected": "Whether the new streets join your road network.",
    "Greenery": "Trees, bushes, flowers and rocks to plant.",
};

/**
 * A description on hover, in the game's tooltip bubble: the setting's own, or the text given for anything else.
 * Without either it is left as it is.
 */
export function SettingTip({ label, text: given, children }: { label?: string; text?: string; children: React.ReactElement }) {
    const text = given ?? (label ? SETTING_TIPS[label] : undefined);
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
