// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
import React, { useRef, useState } from "react";
import * as ui from "cs2/ui";
import { act, SettingTrigger, useToolState } from "./lib/bindings";
import { ErrorBoundary } from "./lib/errorBoundary";
import { statusLine, ToolState, Zone } from "./lib/state";
import { vanilla } from "./lib/vanilla";
import styles from "./Panel.module.css";

function Button({ children, onSelect, disabled = false, selected = false, primary = false, label }: React.PropsWithChildren<{
    onSelect: () => void; disabled?: boolean; selected?: boolean; primary?: boolean; label?: string;
}>) {
    const className = [styles.button, primary ? styles.primary : "", selected ? styles.selected : "", disabled ? styles.disabled : ""].join(" ");
    return <ui.Button className={className} disabled={disabled} selected={selected} aria-label={label}
        onSelect={() => { if (!disabled) onSelect(); }}>{children}</ui.Button>;
}

function NumberControl({ label, value, minimum, maximum, step, unit = "", trigger, disabled }: {
    label: string; value: number; minimum: number; maximum: number; step: number; unit?: string; trigger: SettingTrigger; disabled: boolean;
}) {
    const [drag, setDrag] = useState<number | null>(null);
    const dragging = useRef(false);
    const pending = useRef(value);
    const snap = (raw: number) => Number(Math.max(minimum, Math.min(maximum, minimum + Math.round((raw - minimum) / step) * step)).toFixed(6));
    const commit = (raw: number) => {
        if (!disabled && Number.isFinite(raw)) act(trigger, snap(raw));
    };
    const shown = drag ?? value;
    const digits = step < 0.1 ? 2 : step < 1 ? 1 : 0;
    const Slider = vanilla.Slider;
    return <div className={styles.control}>
        <div className={styles.controlHeading}><span>{label}</span><span className={styles.value}>{shown.toFixed(digits)}{unit}</span></div>
        <div className={styles.numberRow}>
            <Button label={`Decrease ${label}`} disabled={disabled || shown <= minimum} onSelect={() => commit(shown - step)}>−</Button>
            <div className={`${styles.slider} ${disabled ? styles.disabled : ""}`}>
                {Slider && !disabled ? <Slider focusKey={vanilla.focusDisabled} value={shown} start={minimum} end={maximum} gamepadStep={step}
                    valueTransformer={(start, end, ratio) => snap(start + ratio * (end - start))}
                    onDragStart={() => { dragging.current = true; pending.current = value; }}
                    onChange={next => {
                        if (!Number.isFinite(next)) return;
                        if (dragging.current) { pending.current = snap(next); setDrag(pending.current); }
                        else commit(next);
                    }}
                    onDragEnd={() => { dragging.current = false; setDrag(null); if (pending.current !== value) commit(pending.current); }}
                /> : <div className={styles.track}><div className={styles.progress} style={{ width: `${100 * (shown - minimum) / (maximum - minimum)}%` }} /></div>}
            </div>
            <Button label={`Increase ${label}`} disabled={disabled || shown >= maximum} onSelect={() => commit(shown + step)}>+</Button>
        </div>
    </div>;
}

function ZoneControl({ zones, value, disabled }: { zones: Zone[]; value: string; disabled: boolean }) {
    const options = zones.some(zone => zone.name === value) ? zones : [{ name: value, label: value }, ...zones];
    const label = options.find(zone => zone.name === value)?.label ?? value;
    if (!ui.Dropdown || !ui.DropdownToggle || !ui.DropdownItem) return <div className={styles.control}>
        <div>Zone</div>{options.map(zone => <Button key={zone.name} disabled={disabled} selected={zone.name === value}
            onSelect={() => act("setZone", zone.name)}>{zone.label}</Button>)}
    </div>;
    return <div className={styles.control}>
        <div className={styles.controlHeading}>Zone</div>
        {disabled ? <div className={`${styles.dropdownToggle} ${styles.disabled}`}>{label}</div> : <ui.Dropdown
            theme={{ dropdownToggle: styles.dropdownToggle, dropdownPopup: styles.dropdownPopup, dropdownMenu: styles.dropdownMenu, dropdownItem: styles.dropdownItem }}
            content={options.map(zone => <ui.DropdownItem key={zone.name} value={zone.name} selected={zone.name === value}
                onChange={name => act("setZone", name)}>{zone.label}</ui.DropdownItem>)}>
            <ui.DropdownToggle>{label}</ui.DropdownToggle>
        </ui.Dropdown>}
    </div>;
}

function StreetPanel() {
    const state = useToolState();
    const [advanced, setAdvanced] = useState(false);
    const [confirmedPlan, setConfirmedPlan] = useState<ToolState | null>(null);
    if (state.stage === "Closed" || !ui.Button || !ui.Panel) return null;
    const confirming = state.stage === "Planned" && confirmedPlan === state;
    const building = state.stage === "Building";
    const disabled = building || confirming;
    const settings = state.settings;
    return <>
        <ui.Panel className={styles.panel} header="Stepped Streets" theme={{ header: styles.header, content: styles.content }}
            onMouseDown={event => event.stopPropagation()} aria-label="Stepped Streets options" aria-busy={building}>
            <div className={`${styles.status} ${state.hoverTooFlat && state.stage === "Picking" ? styles.tooFlat : ""}`} aria-live="polite">{statusLine(state)}</div>
            <div className={styles.control}>
                <div className={styles.controlHeading}>Side</div>
                <div className={styles.choices}>{(["Both", "Left", "Right"] as const).map(side =>
                    <Button key={side} disabled={disabled} selected={settings.side === side} onSelect={() => act("setSide", side)}>{side}</Button>)}</div>
            </div>
            <ZoneControl zones={state.zones} value={settings.zone} disabled={disabled} />
            <NumberControl label="Lot width" value={settings.lotWidthCells} minimum={2} maximum={6} step={1} unit=" cells" trigger="setLotWidth" disabled={disabled} />
            <div className={styles.control}>
                <div className={styles.controlHeading}>Step look</div>
                <div className={styles.choices}>
                    <Button disabled={disabled} selected={settings.look === "PlantedBank"} onSelect={() => act("setLook", "PlantedBank")}>Planted bank</Button>
                    <Button disabled={disabled} selected={settings.look === "LowWall"} onSelect={() => act("setLook", "LowWall")}>Low wall</Button>
                </div>
            </div>
            <div className={styles.advanced}>
                <ui.Button className={styles.foldout} disabled={disabled} aria-expanded={advanced} onSelect={() => { if (!disabled) setAdvanced(!advanced); }}>
                    {advanced ? "▾" : "▸"} Advanced
                </ui.Button>
                {advanced ? <>
                    <NumberControl label="Narrowest width" value={settings.narrowestWidthCells} minimum={2} maximum={6} step={1} unit=" cells" trigger="setNarrowest" disabled={disabled} />
                    <NumberControl label="Depth" value={settings.depthCells} minimum={2} maximum={6} step={1} unit=" cells" trigger="setDepth" disabled={disabled} />
                    <NumberControl label="Minimum grade" value={settings.minimumGradePercent} minimum={4} maximum={12} step={1} unit="%" trigger="setMinimumGrade" disabled={disabled} />
                    <NumberControl label="Highest step" value={settings.highestStep} minimum={0.5} maximum={5} step={0.1} unit=" m" trigger="setHighestStep" disabled={disabled} />
                    <NumberControl label="Bank grade" value={settings.bankGrade} minimum={0.2} maximum={0.5} step={0.05} trigger="setBankGrade" disabled={disabled} />
                    <NumberControl label="Earthwork limit" value={settings.earthworkLimit} minimum={1} maximum={8} step={0.5} unit=" m" trigger="setEarthworkLimit" disabled={disabled} />
                </> : null}
            </div>
            <div aria-live="polite">
                {state.summary ? <div className={styles.summary}>{state.summary}</div> : null}
                {state.messages.length ? <ul className={styles.messages}>{state.messages.map((message, index) => <li key={index}>{message}</li>)}</ul> : null}
            </div>
            <div className={styles.actions}>
                <Button primary disabled={disabled || state.stage !== "Planned"} onSelect={() => setConfirmedPlan(state)}>{building ? "Building…" : "Build"}</Button>
                <Button disabled={disabled} onSelect={() => { setConfirmedPlan(null); act("clearSelection"); }}>Clear</Button>
                <Button disabled={disabled} onSelect={() => { setConfirmedPlan(null); act("close"); }}>Close</Button>
            </div>
        </ui.Panel>
        {confirming ? <div className={styles.scrim} onMouseDown={event => event.stopPropagation()}>
            <div className={styles.confirmation} role="dialog" aria-modal="true" aria-labelledby="stepped-streets-confirm"
                onKeyDown={event => { if (event.key === "Escape") { event.stopPropagation(); setConfirmedPlan(null); } }}>
                <div className={styles.confirmTitle}>Build stepped lots</div>
                <p id="stepped-streets-confirm">Ground changes are permanent, like the game's terrain tools. Build?</p>
                <div className={styles.actions}>
                    <Button primary onSelect={() => { if (confirmedPlan === state && state.stage === "Planned") act("build"); setConfirmedPlan(null); }}>Build</Button>
                    <Button onSelect={() => setConfirmedPlan(null)}>Cancel</Button>
                </div>
            </div>
        </div> : null}
    </>;
}

export function PanelRoot() {
    return <ErrorBoundary name="panel"><StreetPanel /></ErrorBoundary>;
}
