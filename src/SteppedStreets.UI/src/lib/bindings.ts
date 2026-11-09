// Bindings to Game/UI/PlacemakerUISystem (shapes: ../contract.ts, which is authoritative).
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: one defensive JSON state binding and the Stepped Streets triggers.
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
export type TextTrigger = "setStyle" | "setJunctions" | "setRoad" | "setMixPattern" | "setZoneMix" | "setServiceMix" | "openLink";
export type NumberTrigger = "setBlockDepth" | "setMaxRoadGrade" | "setSteepestBuildable" | "setLotWidth" | "setLotDepth"
    | "setEarthworkLimit" | "setBankGrade" | "setLotSpacing" | "setTreeDensity" | "setSeed";
export type ToggleTrigger = "setLevelLots" | "setRetainingWalls" | "setTrees" | "setSteepGround" | "setShapeGround" | "setExistingStreets";
export function act(name: Action): void;
export function act(name: TextTrigger, value: string): void;
export function act(name: NumberTrigger, value: number): void;
export function act(name: ToggleTrigger | "setConfirming" | "setPanelCollapsed", value: boolean): void;
export function act(name: "setPanelPosition", left: number, top: number): void;
export function act(name: string, ...values: (string | number | boolean)[]): void {
    try { trigger(GROUP, name, ...values); }
    catch (error) { console.error(`[SteppedStreets] Trigger ${name} failed`, error); }
}

/** The game's own UI sounds, through its audio binding; silent when the binding is missing. */
export function playSound(sound: "hover-item" | "select-item" | "toggle-on" | "toggle-off"): void {
    try { trigger("audio", "playSound", sound, 1); }
    catch { /* no audio binding */ }
}
