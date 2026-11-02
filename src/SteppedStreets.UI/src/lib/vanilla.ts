// Vanilla game UI components resolved from the cohtml module registry.
// Adapted from Grid Road Generator (Ti4goc); vanilla.ts pattern from CS2-NetworkTools
// (Luca Rager, MIT, https://github.com/lucarager/CS2-NetworkTools).
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by Tevin Benjamin, 2026-11: resolve only the slider and focus key used by Stepped Streets.
import { ComponentType } from "react";
import { FocusKey } from "cs2/ui";
import { ModuleRegistry } from "cs2/modding";

interface SliderProps {
    focusKey?: FocusKey;
    value: number;
    start: number;
    end: number;
    gamepadStep: number;
    valueTransformer: (start: number, end: number, ratio: number) => number;
    onChange: (value: number) => void;
    onDragStart: () => void;
    onDragEnd: () => void;
}
export const vanilla: { Slider?: ComponentType<SliderProps>; focusDisabled?: FocusKey } = {};
export function initializeVanilla(registry: ModuleRegistry) {
    try { vanilla.Slider = registry.get("game-ui/common/input/slider/slider.tsx", "Slider"); }
    catch (error) { console.warn("[SteppedStreets] Slider unavailable; using step buttons", error); }
    try { vanilla.focusDisabled = registry.get("game-ui/common/focus/focus-key.ts", "FOCUS_DISABLED"); }
    catch (error) { console.warn("[SteppedStreets] Focus key unavailable", error); }
}
