// Bindings to Game/UI/PlacemakerUISystem (shapes: ../contract.ts, which is authoritative).
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by Tevin Benjamin, 2026-11: one defensive JSON state binding and the Stepped Streets triggers.
import { useMemo } from "react";
import { bindValue, trigger, useValue, ValueBinding } from "cs2/api";
import { readState } from "./state";

const GROUP = "SteppedStreets";
const emptyBinding: ValueBinding<unknown> = {
    value: null,
    subscribe: () => ({ value: null, setChangeListener: () => undefined, dispose: () => undefined }),
    dispose: () => undefined,
};
const stateBinding = (() => {
    try { return bindValue<unknown>(GROUP, "state", null); }
    catch (error) { console.warn("[SteppedStreets] State binding unavailable", error); return emptyBinding; }
})();

export function useToolState() {
    const raw = useValue(stateBinding);
    return useMemo(() => readState(raw), [raw]);
}

export type Action = "toggleTool" | "close" | "clearArea" | "closeArea" | "undo" | "build" | "reroll";
export type TextTrigger = "setStyle" | "setRoad" | "setMixPattern" | "setZoneMix";
export type NumberTrigger = "setBlockDepth" | "setMaxRoadGrade" | "setSteepestBuildable" | "setLotWidth" | "setLotDepth"
    | "setEarthworkLimit" | "setBankGrade" | "setLotSpacing" | "setTreeDensity" | "setSeed";
export type ToggleTrigger = "setLevelLots" | "setRetainingWalls" | "setTrees";
export function act(name: Action): void;
export function act(name: TextTrigger, value: string): void;
export function act(name: NumberTrigger, value: number): void;
export function act(name: ToggleTrigger, value: boolean): void;
export function act(name: Action | TextTrigger | NumberTrigger | ToggleTrigger, value?: string | number | boolean): void {
    try {
        if (value === undefined) trigger(GROUP, name);
        else trigger(GROUP, name, value);
    } catch (error) { console.error(`[SteppedStreets] Trigger ${name} failed`, error); }
}

/** The game's own UI sounds, through its audio binding; silent when the binding is missing. */
export function playSound(sound: "hover-item" | "select-item" | "toggle-on" | "toggle-off"): void {
    try { trigger("audio", "playSound", sound, 1); }
    catch { /* no audio binding */ }
}
