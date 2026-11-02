// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
// The look follows Placemaker's panel (jackwrichards, GPL-3.0): a dark panel docked on the right, small-caps sections,
// label-left rows, segmented choices and one accent button in the footer. Plain elements rather than cs2/ui components,
// so nothing depends on the vanilla themes, which left the first version unstyled.
import React, { useRef, useState } from "react";
import { act, playSound, SettingTrigger, useToolState } from "./lib/bindings";
import { ErrorBoundary } from "./lib/errorBoundary";
import { statusLine, ToolState, Zone } from "./lib/state";
import { vanilla } from "./lib/vanilla";
import icon from "./assets/stepped-streets.svg";
import closeIcon from "./assets/close.svg";
import chevronIcon from "./assets/chevron.svg";
import styles from "./Panel.module.css";

const classes = (...names: (string | false | undefined)[]) => names.filter(Boolean).join(" ");

function Button({ children, onSelect, disabled = false, selected = false, className, label }: React.PropsWithChildren<{
    onSelect: () => void; disabled?: boolean; selected?: boolean; className?: string; label?: string;
}>) {
    return <button type="button" aria-label={label} aria-pressed={selected}
        className={classes(styles.button, className, selected && styles.selected, disabled && styles.disabled)}
        onMouseEnter={() => { if (!disabled) playSound("hover-item"); }}
        onClick={event => {
            event.stopPropagation();
            if (disabled) return;
            playSound("select-item");
            onSelect();
        }}>{children}</button>;
}

function Row({ label, children }: React.PropsWithChildren<{ label: string }>) {
    return <div className={styles.row}>
        <div className={styles.label}>{label}</div>
        <div className={styles.editor}>{children}</div>
    </div>;
}

function Section({ title, children }: React.PropsWithChildren<{ title: string }>) {
    return <div className={styles.section}>
        <div className={styles.sectionHead}>
            <div className={styles.sectionTitle}>{title}</div>
            <div className={styles.sectionRule} />
        </div>
        {children}
    </div>;
}

function Segmented<T extends string>({ options, value, disabled, onChange }: {
    options: [T, string][]; value: T; disabled: boolean; onChange: (value: T) => void;
}) {
    return <div className={styles.segmented}>
        {options.map(([option, text]) => <Button key={option} className={styles.segment} selected={option === value}
            disabled={disabled} onSelect={() => { if (option !== value) onChange(option); }}>{text}</Button>)}
    </div>;
}

function NumberRow({ label, value, minimum, maximum, step, format, trigger, disabled }: {
    label: string; value: number; minimum: number; maximum: number; step: number;
    format: (value: number) => string; trigger: SettingTrigger; disabled: boolean;
}) {
    const [drag, setDrag] = useState<number | null>(null);
    const dragging = useRef(false);
    const pending = useRef(value);
    const snap = (raw: number) =>
        Number(Math.max(minimum, Math.min(maximum, minimum + Math.round((raw - minimum) / step) * step)).toFixed(6));
    // Settings re-plan the street, so a drag sends one value when it ends rather than one per frame.
    const commit = (raw: number) => {
        const next = snap(raw);
        if (!disabled && Number.isFinite(next) && next !== value) act(trigger, next);
    };
    const shown = drag ?? value;
    const Slider = vanilla.Slider;
    return <Row label={label}>
        <div className={styles.slider}>
            {Slider && !disabled ? <Slider focusKey={vanilla.focusDisabled} value={shown} start={minimum} end={maximum} gamepadStep={step}
                valueTransformer={(start, end, ratio) => snap(start + ratio * (end - start))}
                onDragStart={() => { dragging.current = true; pending.current = value; }}
                onChange={next => {
                    if (!Number.isFinite(next)) return;
                    if (dragging.current) { pending.current = snap(next); setDrag(pending.current); }
                    else commit(next);
                }}
                onDragEnd={() => { dragging.current = false; setDrag(null); commit(pending.current); }}
            /> : <div className={styles.stepper}>
                <Button className={styles.stepButton} label={`Decrease ${label}`} disabled={disabled || shown <= minimum}
                    onSelect={() => commit(shown - step)}>-</Button>
                <div className={styles.track}>
                    <div className={styles.progress} style={{ width: `${100 * (shown - minimum) / (maximum - minimum)}%` }} />
                </div>
                <Button className={styles.stepButton} label={`Increase ${label}`} disabled={disabled || shown >= maximum}
                    onSelect={() => commit(shown + step)}>+</Button>
            </div>}
        </div>
        <div className={styles.value}>{format(shown)}</div>
    </Row>;
}

function ZoneRow({ zones, value, disabled }: { zones: Zone[]; value: string; disabled: boolean }) {
    const [open, setOpen] = useState(false);
    const options = zones.some(zone => zone.name === value) ? zones : [{ name: value, label: value }, ...zones];
    const current = options.find(zone => zone.name === value)?.label ?? value;
    // An inline list instead of a popup: the panel scrolls, and a popup would be clipped by it.
    return <>
        <Row label="Zone">
            <Button className={styles.picker} disabled={disabled} selected={open} onSelect={() => setOpen(!open)}>
                <div className={styles.pickerLabel}>{current}</div>
                <img className={classes(styles.chevron, open && styles.chevronOpen)} src={chevronIcon} />
            </Button>
        </Row>
        {open && !disabled ? <div className={styles.list}>
            {options.map(zone => <Button key={zone.name} className={styles.listItem} selected={zone.name === value}
                onSelect={() => { act("setZone", zone.name); setOpen(false); }}>{zone.label}</Button>)}
        </div> : null}
    </>;
}

const cells = (value: number) => `${Math.round(value)} ${Math.round(value) === 1 ? "cell" : "cells"}`;
const percent = (value: number) => `${Math.round(value)}%`;
const metres = (value: number) => `${value.toFixed(1)} m`;
const slope = (value: number) => `${Math.round(value * 100)}%`;

function statusTone(state: ToolState) {
    if (state.stage === "Failed") return styles.toneBad;
    if (state.stage === "Building") return styles.toneBusy;
    if (state.stage === "Planned" || state.stage === "Built") return styles.toneGood;
    if (state.hoverGradePercent !== null) return state.hoverTooFlat ? styles.toneFlat : styles.toneGood;
    return styles.toneIdle;
}

function StreetPanel() {
    const state = useToolState();
    const [advanced, setAdvanced] = useState(false);
    const [confirmedPlan, setConfirmedPlan] = useState<ToolState | null>(null);
    if (state.stage === "Closed") return null;
    const confirming = state.stage === "Planned" && confirmedPlan === state;
    const building = state.stage === "Building";
    const disabled = building || confirming;
    const settings = state.settings;
    // The tool also lists its summary among the messages; showing it twice reads as noise.
    const messages = state.messages.filter(message => message !== state.summary);
    const buildLabel = building ? "Building..." : state.stage === "Built" ? "Built" : "Build";

    return <div className={styles.dock}>
        <div className={styles.wrap}>
            <div className={styles.panel} onMouseDown={event => event.stopPropagation()} aria-busy={building}>
                <div className={styles.header}>
                    <div className={styles.accentBar} />
                    <img className={styles.headerIcon} src={icon} />
                    <div className={styles.title}>Stepped Streets</div>
                    <Button className={styles.close} label="Close" disabled={building}
                        onSelect={() => { setConfirmedPlan(null); act("close"); }}>
                        <img className={styles.closeIcon} src={closeIcon} />
                    </Button>
                </div>

                <div className={styles.content}>
                    <div className={classes(styles.status, statusTone(state))} aria-live="polite">
                        <div className={styles.statusText}>{statusLine(state)}</div>
                        {state.stage === "Planned"
                            ? <div className={styles.statusHint}>Shift-click adds the next segment. Right-click clears.</div>
                            : state.stage === "Picking" && state.hoverGradePercent === null
                                ? <div className={styles.statusHint}>Esc closes the tool.</div> : null}
                    </div>

                    <Section title="Lots">
                        <Row label="Side">
                            <Segmented options={[["Both", "Both"], ["Left", "Left"], ["Right", "Right"]]} value={settings.side}
                                disabled={disabled} onChange={side => act("setSide", side)} />
                        </Row>
                        <ZoneRow zones={state.zones} value={settings.zone} disabled={disabled} />
                        <NumberRow label="Lot width" value={settings.lotWidthCells} minimum={2} maximum={6} step={1}
                            format={cells} trigger="setLotWidth" disabled={disabled} />
                        <Row label="Step look">
                            <Segmented options={[["PlantedBank", "Planted bank"], ["LowWall", "Low wall"]]} value={settings.look}
                                disabled={disabled} onChange={look => act("setLook", look)} />
                        </Row>
                    </Section>

                    <div className={styles.fold}>
                        <Button className={styles.foldHead} disabled={disabled} selected={false} onSelect={() => setAdvanced(!advanced)}>
                            <img className={classes(styles.chevron, !advanced && styles.chevronClosed)} src={chevronIcon} />
                            <div className={styles.foldTitle}>Advanced</div>
                            <div className={styles.foldSummary}>{advanced ? "" :
                                `${cells(settings.depthCells)} deep, ${percent(settings.minimumGradePercent)} min, ${metres(settings.highestStep)} step`}</div>
                        </Button>
                        {advanced ? <div className={styles.foldBody}>
                            <NumberRow label="Narrowest width" value={settings.narrowestWidthCells} minimum={2} maximum={6} step={1}
                                format={cells} trigger="setNarrowest" disabled={disabled} />
                            <NumberRow label="Depth" value={settings.depthCells} minimum={2} maximum={6} step={1}
                                format={cells} trigger="setDepth" disabled={disabled} />
                            <NumberRow label="Minimum grade" value={settings.minimumGradePercent} minimum={4} maximum={12} step={1}
                                format={percent} trigger="setMinimumGrade" disabled={disabled} />
                            <NumberRow label="Highest step" value={settings.highestStep} minimum={0.5} maximum={5} step={0.1}
                                format={metres} trigger="setHighestStep" disabled={disabled} />
                            <NumberRow label="Bank grade" value={settings.bankGrade} minimum={0.2} maximum={0.5} step={0.05}
                                format={slope} trigger="setBankGrade" disabled={disabled} />
                            <NumberRow label="Earthwork limit" value={settings.earthworkLimit} minimum={1} maximum={8} step={0.5}
                                format={metres} trigger="setEarthworkLimit" disabled={disabled} />
                        </div> : null}
                    </div>

                    {state.summary || messages.length ? <Section title={state.stage === "Built" ? "Result" : "Plan"}>
                        {state.summary ? <div className={classes(styles.summary, state.stage === "Failed" && styles.summaryBad)}>
                            {state.summary}</div> : null}
                        {messages.map((message, index) => <div key={index} className={styles.message}>
                            <div className={styles.messageDot} />
                            <div className={styles.messageText}>{message}</div>
                        </div>)}
                    </Section> : null}
                </div>

                <div className={styles.footer}>
                    <Button className={styles.secondary} disabled={disabled || (!state.streetName && state.stage === "Picking")}
                        onSelect={() => { setConfirmedPlan(null); act("clearSelection"); }}>Clear</Button>
                    <Button className={styles.primary} disabled={disabled || state.stage !== "Planned"}
                        onSelect={() => setConfirmedPlan(state)}>{buildLabel}</Button>
                </div>

                {confirming ? <div className={styles.backdrop}>
                    <div className={styles.dialog} role="dialog" aria-modal="true" aria-labelledby="stepped-streets-confirm">
                        <div className={styles.dialogTitle}>Build stepped lots?</div>
                        <div id="stepped-streets-confirm" className={styles.dialogText}>
                            Ground changes are permanent, like the game's terrain tools. Build?</div>
                        <div className={styles.dialogButtons}>
                            <Button className={styles.secondary} onSelect={() => setConfirmedPlan(null)}>Cancel</Button>
                            <Button className={styles.primary} onSelect={() => {
                                if (confirmedPlan === state && state.stage === "Planned") act("build");
                                setConfirmedPlan(null);
                            }}>Build</Button>
                        </div>
                    </div>
                </div> : null}
            </div>
        </div>
    </div>;
}

export function PanelRoot() {
    return <ErrorBoundary name="panel"><StreetPanel /></ErrorBoundary>;
}
