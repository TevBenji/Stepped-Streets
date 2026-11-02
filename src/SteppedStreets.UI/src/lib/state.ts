// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
export type ToolStage = "Closed" | "Picking" | "Planned" | "Building" | "Built" | "Failed";
export type Side = "Both" | "Left" | "Right";
export type StepLook = "PlantedBank" | "LowWall";

export interface Settings {
    side: Side;
    zone: string;
    lotWidthCells: number;
    narrowestWidthCells: number;
    depthCells: number;
    look: StepLook;
    minimumGradePercent: number;
    highestStep: number;
    bankGrade: number;
    earthworkLimit: number;
}

export interface Zone { name: string; label: string }
export interface ToolState {
    stage: ToolStage;
    streetName: string;
    hoverGradePercent: number | null;
    hoverTooFlat: boolean;
    summary: string;
    messages: string[];
    settings: Settings;
    zones: Zone[];
}

const object = (value: unknown): Record<string, unknown> =>
    value !== null && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : {};
const text = (value: unknown, fallback = ""): string => typeof value === "string" ? value : fallback;
const number = (value: unknown, fallback: number, minimum: number, maximum: number): number =>
    typeof value === "number" && Number.isFinite(value) ? Math.max(minimum, Math.min(maximum, value)) : fallback;
function choice<T extends string>(value: unknown, choices: readonly T[], fallback: T): T {
    return choices.includes(value as T) ? value as T : fallback;
}

export function readState(raw: unknown): ToolState {
    let parsed = raw;
    try { if (typeof raw === "string") parsed = JSON.parse(raw); }
    catch { parsed = null; }
    const state = object(parsed);
    const settings = object(state.settings);
    const zones: Zone[] = [];
    if (Array.isArray(state.zones)) {
        for (const value of state.zones) {
            const zone = object(value);
            if (typeof zone.name === "string" && zone.name.length > 0 && !zones.some(entry => entry.name === zone.name))
                zones.push({ name: zone.name, label: text(zone.label, zone.name) });
        }
    }
    return {
        stage: choice(state.stage, ["Closed", "Picking", "Planned", "Building", "Built", "Failed"], "Closed"),
        streetName: text(state.streetName),
        hoverGradePercent: typeof state.hoverGradePercent === "number" && Number.isFinite(state.hoverGradePercent)
            ? state.hoverGradePercent : null,
        hoverTooFlat: state.hoverTooFlat === true,
        summary: text(state.summary),
        messages: Array.isArray(state.messages) ? state.messages.filter((value): value is string => typeof value === "string") : [],
        settings: {
            side: choice(settings.side, ["Both", "Left", "Right"], "Both"),
            zone: text(settings.zone, "Residential Low"),
            lotWidthCells: Math.round(number(settings.lotWidthCells, 3, 2, 6)),
            narrowestWidthCells: Math.round(number(settings.narrowestWidthCells, 3, 2, 6)),
            depthCells: Math.round(number(settings.depthCells, 3, 2, 6)),
            look: choice(settings.look, ["PlantedBank", "LowWall"], "PlantedBank"),
            minimumGradePercent: Math.round(number(settings.minimumGradePercent, 6, 4, 12)),
            highestStep: number(settings.highestStep, 3.2, 0.5, 5),
            bankGrade: number(settings.bankGrade, 0.4, 0.2, 0.5),
            earthworkLimit: number(settings.earthworkLimit, 4, 1, 8),
        },
        zones,
    };
}

export function statusLine(state: ToolState): string {
    if (state.stage === "Building") return "Building…";
    if (state.stage === "Picking" && state.hoverGradePercent !== null)
        return state.hoverTooFlat ? `Too flat: under ${state.settings.minimumGradePercent}%` : `${Number(state.hoverGradePercent.toFixed(1))}% climb`;
    if (state.streetName) return state.streetName;
    return "Click a street. Shift-click adds the next one.";
}
