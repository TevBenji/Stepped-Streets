// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
export type ToolStage = "Closed" | "Drawing" | "Planning" | "Planned" | "Building" | "Built" | "Failed";
export type NeighbourhoodStyle = "ContourSuburb" | "TerracedBlocks" | "MountainLane"
    | "SquareGrid" | "RectangularGrid" | "IrregularGrid" | "SkewedGrid" | "FragmentedGrid"
    | "DiagonalGrid" | "CurvedGrid" | "WarpedGrid" | "RadialGrid" | "Star" | "Rings" | "Spiderweb" | "Fan"
    | "Spine" | "Ladder" | "Comb" | "Fishbone" | "Tree" | "CulDeSacs" | "Loops" | "LoopsAndLollipops" | "Lollipops"
    | "TriangularMesh" | "Hexagonal" | "HexagonalSpokes" | "Ridge" | "Valley" | "Coastal" | "Winding"
    | "OrganicMesh" | "Hybrid" | "Polycentric" | "Superblock" | "Radburn" | "FusedGrid" | "FilteredGrid" | "BarcelonaSuperblock";
export const STYLES: NeighbourhoodStyle[] = ["ContourSuburb", "TerracedBlocks", "MountainLane",
    "SquareGrid", "RectangularGrid", "IrregularGrid", "SkewedGrid", "FragmentedGrid",
    "DiagonalGrid", "CurvedGrid", "WarpedGrid", "RadialGrid", "Star", "Rings", "Spiderweb", "Fan",
    "Spine", "Ladder", "Comb", "Fishbone", "Tree", "CulDeSacs", "Loops", "LoopsAndLollipops", "Lollipops",
    "TriangularMesh", "Hexagonal", "HexagonalSpokes", "Ridge", "Valley", "Coastal", "Winding",
    "OrganicMesh", "Hybrid", "Polycentric", "Superblock", "Radburn", "FusedGrid", "FilteredGrid", "BarcelonaSuperblock"];
const JUNCTION_STYLES: NeighbourhoodStyle[] = ["SquareGrid", "RectangularGrid", "IrregularGrid", "SkewedGrid", "FragmentedGrid", "DiagonalGrid", "CurvedGrid", "WarpedGrid", "RadialGrid"];
/** The layouts tried in real cities; the rest are marked experimental until they have been. */
const TRIED_STYLES: NeighbourhoodStyle[] = ["ContourSuburb", "TerracedBlocks", "MountainLane"];
export const isExperimental = (style: NeighbourhoodStyle) => !TRIED_STYLES.includes(style);
/** The grid layouts, whose cross streets can meet as crossroads, staggered or traffic-optimised. */
export const usesJunctions = (style: NeighbourhoodStyle) => JUNCTION_STYLES.includes(style);
export type JunctionPattern = "Crossroads" | "Staggered" | "TrafficOptimised";
export type MixPattern = "Random" | "Clumps" | "Even";

/** A catalogue entry; icon is the game's own menu icon for it, when the game has one; group heads its part of a list. */
export interface Option { name: string; label: string; icon?: string; group?: string }
export interface ZoneShare { zone: string; share: number }
export interface ZoneCount { zone: string; lots: number }
/** A service building the game offers: its lot in cells and the city service it belongs to. */
export interface ServiceOption { name: string; label: string; group: string; width: number; depth: number; icon?: string }
/** How many of one service building: in the mix, or in the plan. */
export interface ServiceCount { name: string; count: number }

export interface Settings {
    style: NeighbourhoodStyle;
    road: string;
    blockDepth: number;
    maxRoadGradePercent: number;
    steepestBuildablePercent: number;
    /** Steep hills and mountains: buildable ground up to 60 % and earthwork up to 12 m. */
    steepGround: boolean;
    /** Change the terrain at all; off, nothing is graded. */
    shapeGround: boolean;
    /** How a grid's cross streets meet its main streets. */
    junctions: JunctionPattern;
    zoneMix: ZoneShare[];
    serviceMix: ServiceCount[];
    mixPattern: MixPattern;
    lotWidthCells: number;
    lotDepthCells: number;
    levelLots: boolean;
    earthworkLimit: number;
    bankGradePercent: number;
    /** -1 lets the planner choose; 0-2 are cells between lots. */
    lotSpacing: number;
    retainingWalls: boolean;
    trees: boolean;
    treeDensityPercent: number;
    seed: number;
}

export interface Stats {
    roadKilometres: number;
    lots: number;
    zonedCells: number;
    earthCubicMetres: number;
    steepestRoadPercent: number;
    wildPercent: number;
    connected: boolean;
    trees: number;
}

export interface ToolState {
    stage: ToolStage;
    corners: number;
    areaClosed: boolean;
    areaSquareMetres: number;
    summary: string;
    messages: string[];
    stats: Stats;
    zoneCounts: ZoneCount[];
    /** 0-1 while Building. */
    progress: number;
    settings: Settings;
    zones: Option[];
    roads: Option[];
    services: ServiceOption[];
    serviceCounts: ServiceCount[];
}

// Counters and areas have no natural ceiling; this only keeps absurd values out of the layout.
const LARGEST = 1e12;
const DEFAULT_MIX: ZoneShare[] = [{ zone: "Residential Low", share: 100 }];

const object = (value: unknown): Record<string, unknown> =>
    value !== null && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : {};
const text = (value: unknown, fallback = ""): string => typeof value === "string" ? value : fallback;
const flag = (value: unknown, fallback: boolean): boolean => typeof value === "boolean" ? value : fallback;
const number = (value: unknown, fallback: number, minimum: number, maximum: number): number =>
    typeof value === "number" && Number.isFinite(value) ? Math.max(minimum, Math.min(maximum, value)) : fallback;
const whole = (value: unknown, fallback: number, minimum: number, maximum: number): number =>
    Math.round(number(value, fallback, minimum, maximum));
function choice<T extends string>(value: unknown, choices: readonly T[], fallback: T): T {
    return choices.includes(value as T) ? value as T : fallback;
}

/** Valid rows only, first one wins for a repeated key. */
function rows<T>(value: unknown, read: (row: Record<string, unknown>) => T | null, key: (row: T) => string): T[] {
    const result: T[] = [];
    if (!Array.isArray(value)) return result;
    for (const entry of value) {
        const row = read(object(entry));
        if (row !== null && !result.some(kept => key(kept) === key(row))) result.push(row);
    }
    return result;
}

// Only present when sent, so a catalogue without icons reads exactly as before.
const withIcon = <T extends object>(value: T, icon: unknown): T => (text(icon) ? { ...value, icon: text(icon) } : value);
const readOption = (row: Record<string, unknown>): Option | null =>
    typeof row.name === "string" && row.name.length > 0 ? withIcon({ name: row.name, label: text(row.label) || row.name }, row.icon) : null;
const readShare = (row: Record<string, unknown>): ZoneShare | null =>
    typeof row.zone === "string" && row.zone.length > 0 && typeof row.share === "number" && Number.isFinite(row.share) && row.share > 0
        ? { zone: row.zone, share: Math.min(100, row.share) } : null;
const readServiceCount = (row: Record<string, unknown>): ServiceCount | null =>
    typeof row.name === "string" && row.name.length > 0 && typeof row.count === "number" && Number.isFinite(row.count) && row.count >= 1
        ? { name: row.name, count: whole(row.count, 1, 1, 20) } : null;
const readService = (row: Record<string, unknown>): ServiceOption | null =>
    typeof row.name === "string" && row.name.length > 0
        ? withIcon({ name: row.name, label: text(row.label) || row.name, group: text(row.group), width: whole(row.width, 4, 1, 20), depth: whole(row.depth, 4, 1, 20) }, row.icon)
        : null;
const readCount = (row: Record<string, unknown>): ZoneCount | null =>
    typeof row.zone === "string" && row.zone.length > 0 && typeof row.lots === "number" && Number.isFinite(row.lots)
        ? { zone: row.zone, lots: whole(row.lots, 0, 0, LARGEST) } : null;

export function readState(raw: unknown): ToolState {
    let parsed = raw;
    try { if (typeof raw === "string") parsed = JSON.parse(raw); }
    catch { parsed = null; }
    const state = object(parsed);
    const settings = object(state.settings);
    const stats = object(state.stats);
    const zoneMix = rows(settings.zoneMix, readShare, share => share.zone);
    return {
        stage: choice(state.stage, ["Closed", "Drawing", "Planning", "Planned", "Building", "Built", "Failed"], "Closed"),
        corners: whole(state.corners, 0, 0, LARGEST),
        areaClosed: flag(state.areaClosed, false),
        areaSquareMetres: number(state.areaSquareMetres, 0, 0, LARGEST),
        summary: text(state.summary),
        messages: Array.isArray(state.messages) ? state.messages.filter((value): value is string => typeof value === "string") : [],
        stats: {
            roadKilometres: number(stats.roadKilometres, 0, 0, LARGEST),
            lots: whole(stats.lots, 0, 0, LARGEST),
            zonedCells: whole(stats.zonedCells, 0, 0, LARGEST),
            earthCubicMetres: number(stats.earthCubicMetres, 0, 0, LARGEST),
            steepestRoadPercent: number(stats.steepestRoadPercent, 0, 0, LARGEST),
            wildPercent: number(stats.wildPercent, 0, 0, 100),
            connected: flag(stats.connected, false),
            trees: whole(stats.trees, 0, 0, LARGEST),
        },
        zoneCounts: rows(state.zoneCounts, readCount, count => count.zone),
        progress: number(state.progress, 0, 0, 1),
        settings: {
            style: choice(settings.style, STYLES, "ContourSuburb"),
            road: text(settings.road) || "Small Road",
            blockDepth: number(settings.blockDepth, 90, 60, 200),
            maxRoadGradePercent: number(settings.maxRoadGradePercent, 8, 4, 12),
            steepestBuildablePercent: number(settings.steepestBuildablePercent, 15, 8, 60),
            steepGround: flag(settings.steepGround, false),
            shapeGround: flag(settings.shapeGround, true),
            junctions: choice(settings.junctions, ["Crossroads", "Staggered", "TrafficOptimised"], "Staggered"),
            zoneMix: zoneMix.length > 0 ? zoneMix : DEFAULT_MIX.map(share => ({ ...share })),
            serviceMix: rows(settings.serviceMix, readServiceCount, service => service.name),
            mixPattern: choice(settings.mixPattern, ["Random", "Clumps", "Even"], "Clumps"),
            lotWidthCells: whole(settings.lotWidthCells, 3, 2, 6),
            lotDepthCells: whole(settings.lotDepthCells, 4, 2, 6),
            levelLots: flag(settings.levelLots, true),
            earthworkLimit: number(settings.earthworkLimit, 4, 1, 12),
            bankGradePercent: number(settings.bankGradePercent, 40, 20, 50),
            lotSpacing: whole(settings.lotSpacing, -1, -1, 2),
            retainingWalls: flag(settings.retainingWalls, false),
            trees: flag(settings.trees, false),
            treeDensityPercent: number(settings.treeDensityPercent, 50, 10, 100),
            seed: whole(settings.seed, 1, 1, 99999),
        },
        zones: rows(state.zones, readOption, option => option.name),
        roads: rows(state.roads, readOption, option => option.name),
        services: rows(state.services, readService, service => service.name),
        serviceCounts: rows(state.serviceCounts, readServiceCount, service => service.name),
    };
}

/**
 * The smaller line under the status: the tool's step while it works ("Zoning Residential Low...") and, once built, its build
 * report (what was really built, zoned and planted).
 */
export function statusDetail(state: ToolState): string {
    return state.stage === "Planning" || state.stage === "Building" || state.stage === "Built" ? state.summary : "";
}

export function statusLine(state: ToolState): string {
    switch (state.stage) {
        case "Drawing":
            if (state.corners < 3) return "Click the ground to place corners.";
            // ponytail: a closed area still in Drawing has no buildable plan; the tool's summary says why, if it sent one.
            return state.areaClosed ? state.summary || "Area closed" : "Click the first corner, or press Close area, to finish.";
        case "Planning": return "Planning...";
        case "Planned": return "Neighbourhood planned";
        case "Building": return "Building...";
        case "Built": return "Built";
        case "Failed": return state.summary || "Failed";
        default: return "";
    }
}

/** "3.5 ha", "35 ha", "1.2 km2". The game's font has no superscript digits; the panel raises the trailing 2 itself. */
export function formatArea(squareMetres: number): string {
    const hectares = Math.max(0, squareMetres || 0) / 10000;
    if (hectares < 9.95) return `${hectares.toFixed(1)} ha`;
    if (hectares < 99.5) return `${Math.round(hectares)} ha`;
    return `${(hectares / 100).toFixed(1)} km2`;
}

/** 1240 -> "1,240". Written out because Gameface's Intl support is not something to rely on. */
export function thousands(value: number): string {
    return String(Math.round(value || 0)).replace(/\B(?=(\d{3})+(?!\d))/g, ",");
}

/** The legend colour for a zone, from keywords in its prefab name. */
export function zoneColour(name: string): string {
    const lower = name.toLowerCase();
    if (lower.includes("commercial")) return "#4aa3f0";
    if (lower.includes("office")) return "#b07cf0";
    if (lower.includes("industr")) return "#f0cf4a";
    if (lower.includes("residential")) {
        if (lower.includes("high")) return "#2f8f4e";
        if (lower.includes("medium") || lower.includes("row")) return "#5cbf5c";
        if (lower.includes("low")) return "#a6e07a";
    }
    return "#9aa5ab";
}
