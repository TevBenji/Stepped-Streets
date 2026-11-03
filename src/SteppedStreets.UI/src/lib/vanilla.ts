// Vanilla game UI components resolved from the cohtml module registry.
// Adapted from Grid Road Generator (Ti4goc); vanilla.ts pattern from CS2-NetworkTools
// (Luca Rager, MIT, https://github.com/lucarager/CS2-NetworkTools).
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: resolve only the slider, text input and focus key used by Stepped Streets.
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
    disabled?: boolean;
}
// The game's text box: while it has focus it holds back the game's key shortcuts, so typing doesn't move the camera.
interface TextInputProps {
    value: string;
    placeholder?: string;
    className?: string;
    onChange: (event: { target: { value: string } }) => void;
}
export const vanilla: { Slider?: ComponentType<SliderProps>; TextInput?: ComponentType<TextInputProps>; focusDisabled?: FocusKey } = {};
export function initializeVanilla(registry: ModuleRegistry) {
    try { vanilla.Slider = registry.get("game-ui/common/input/slider/slider.tsx", "Slider"); }
    catch (error) { console.warn("[SteppedStreets] Slider unavailable; using step buttons", error); }
    // No fallback to a plain input: without the game's, typed keys would also reach the game's shortcuts.
    try { vanilla.TextInput = registry.get("game-ui/common/input/text/text-input.tsx", "TextInput"); }
    catch (error) { console.warn("[SteppedStreets] Text input unavailable; lists show without a search box", error); }
    try { vanilla.focusDisabled = registry.get("game-ui/common/focus/focus-key.ts", "FOCUS_DISABLED"); }
    catch (error) { console.warn("[SteppedStreets] Focus key unavailable", error); }
}
