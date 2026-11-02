// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
// The look follows Placemaker's panel (jackwrichards, GPL-3.0): a dark panel docked on the right, small-caps sections,
// label-left rows, segmented choices and one accent button in the footer. Plain elements rather than cs2/ui components,
// so nothing depends on the vanilla themes, which left the first version unstyled.
import React, { useEffect, useRef, useState } from "react";
import { act, NumberTrigger, playSound, ToggleTrigger, useToolState } from "./lib/bindings";
import { ErrorBoundary } from "./lib/errorBoundary";
import { formatArea, MixPattern, NeighbourhoodStyle, Option, statusDetail, statusLine, thousands, ToolState, zoneColour, ZoneShare } from "./lib/state";
import { vanilla } from "./lib/vanilla";
import icon from "./assets/stepped-streets.svg";
import closeIcon from "./assets/close.svg";
import chevronIcon from "./assets/chevron.svg";
import diceIcon from "./assets/dice.svg";
import plusIcon from "./assets/plus.svg";
import styles from "./Panel.module.css";

const classes = (...names: (string | false | undefined)[]) => names.filter(Boolean).join(" ");

function Button({ children, onSelect, disabled = false, selected = false, className, label, sound = "select-item" }: React.PropsWithChildren<{
    onSelect: () => void; disabled?: boolean; selected?: boolean; className?: string; label?: string;
    sound?: "select-item" | "toggle-on" | "toggle-off";
}>) {
    return <button type="button" aria-label={label} aria-pressed={selected}
        className={classes(styles.button, className, selected && styles.selected, disabled && styles.disabled)}
        onMouseEnter={() => { if (!disabled) playSound("hover-item"); }}
        onClick={event => {
            event.stopPropagation();
            if (disabled) return;
            playSound(sound);
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

interface NumberProps {
    label: string; value: number; minimum: number; maximum: number; step: number;
    format: (value: number) => string; disabled: boolean;
}

function NumberControl({ label, value, minimum, maximum, step, format, disabled, onCommit }: NumberProps & {
    onCommit: (value: number) => void;
}) {
    const [drag, setDrag] = useState<number | null>(null);
    const dragging = useRef(false);
    const pending = useRef(value);
    // A released drag keeps showing its value until the tool sends it back, so the thumb doesn't jump to the old one first.
    useEffect(() => { if (!dragging.current) setDrag(null); }, [value]);
    const snap = (raw: number) =>
        Number(Math.max(minimum, Math.min(maximum, minimum + Math.round((raw - minimum) / step) * step)).toFixed(6));
    // Settings re-plan the area, so a drag sends one value when it ends rather than one per frame.
    const commit = (raw: number) => {
        const next = snap(raw);
        if (!disabled && Number.isFinite(next) && next !== value) { onCommit(next); return true; }
        return false;
    };
    const shown = drag ?? value;
    const filled = Math.max(0, Math.min(100, 100 * (shown - minimum) / (maximum - minimum)));
    const Slider = vanilla.Slider;
    return <>
        <div className={styles.slider}>
            {Slider ? <Slider focusKey={vanilla.focusDisabled} value={shown} start={minimum} end={maximum} gamepadStep={step} disabled={disabled}
                valueTransformer={(start, end, ratio) => snap(start + ratio * (end - start))}
                onDragStart={() => { dragging.current = true; pending.current = value; }}
                onChange={next => {
                    if (!Number.isFinite(next)) return;
                    if (dragging.current) { pending.current = snap(next); setDrag(pending.current); }
                    else commit(next);
                }}
                onDragEnd={() => { dragging.current = false; if (!commit(pending.current)) setDrag(null); }}
            /> : <div className={styles.stepper}>
                <Button className={styles.stepButton} label={`Decrease ${label}`} disabled={disabled || shown <= minimum}
                    onSelect={() => commit(shown - step)}>-</Button>
                <div className={styles.track}>
                    <div className={styles.progress} style={{ width: `${filled}%` }} />
                </div>
                <Button className={styles.stepButton} label={`Increase ${label}`} disabled={disabled || shown >= maximum}
                    onSelect={() => commit(shown + step)}>+</Button>
            </div>}
        </div>
        <div className={styles.value}>{format(shown)}</div>
    </>;
}

function NumberRow({ trigger, ...props }: NumberProps & { trigger: NumberTrigger }) {
    return <Row label={props.label}><NumberControl {...props} onCommit={value => act(trigger, value)} /></Row>;
}

function ToggleRow({ label, value, disabled, trigger }: { label: string; value: boolean; disabled: boolean; trigger: ToggleTrigger }) {
    return <Row label={label}>
        <Button className={styles.toggle} label={label} selected={value} disabled={disabled}
            sound={value ? "toggle-off" : "toggle-on"} onSelect={() => act(trigger, !value)}>
            <div className={classes(styles.toggleTrack, value && styles.toggleOn)}><div className={styles.toggleKnob} /></div>
            <div className={styles.toggleText}>{value ? "On" : "Off"}</div>
        </Button>
    </Row>;
}

/** A button that opens an inline list. Without a label it spans the row, and children sit beside it. */
function Picker({ label, options, value, disabled, onPick, children }: React.PropsWithChildren<{
    label?: string; options: Option[]; value: string; disabled: boolean; onPick: (name: string) => void;
}>) {
    const [open, setOpen] = useState(false);
    // Disabling closes the list, so it doesn't pop back open after the build or the confirmation.
    useEffect(() => { if (disabled) setOpen(false); }, [disabled]);
    // A saved choice the game no longer lists still shows, so the button never reads blank.
    const shown = options.some(option => option.name === value) ? options : [{ name: value, label: value }, ...options];
    const current = shown.find(option => option.name === value)?.label ?? value;
    // An inline list instead of a popup: the panel scrolls, and a popup would be clipped by it.
    return <>
        <div className={styles.row}>
            {label ? <div className={styles.label}>{label}</div> : null}
            <Button className={styles.picker} disabled={disabled} selected={open} onSelect={() => setOpen(!open)}>
                <div className={styles.pickerLabel}>{current}</div>
                <img className={classes(styles.chevron, open && styles.chevronOpen)} src={chevronIcon} />
            </Button>
            {children}
        </div>
        {open && !disabled ? <div className={classes(styles.list, !label && styles.listFull)}>
            {shown.map(option => <Button key={option.name} className={styles.listItem} selected={option.name === value}
                onSelect={() => { setOpen(false); if (option.name !== value) onPick(option.name); }}>{option.label}</Button>)}
        </div> : null}
    </>;
}

function ZoneMix({ mix, zones, disabled }: { mix: ZoneShare[]; zones: Option[]; disabled: boolean }) {
    // The tool takes the whole mix at once, so every edit sends the full new list.
    const send = (next: ZoneShare[]) => act("setZoneMix", JSON.stringify(next.map(({ zone, share }) => ({ zone, share }))));
    const total = mix.reduce((sum, entry) => sum + entry.share, 0);
    const labelOf = (name: string) => zones.find(zone => zone.name === name)?.label ?? name;
    const unused = zones.find(zone => !mix.some(entry => entry.zone === zone.name));
    // The tool drops zones the game doesn't have (a removed asset pack), so a mix of only those would be refused.
    const known = (name: string) => zones.length === 0 || zones.some(zone => zone.name === name);
    return <>
        {mix.map((entry, index) => {
            const replace = (change: Partial<ZoneShare>) => send(mix.map((other, at) => at === index ? { ...other, ...change } : other));
            const missing = !known(entry.zone);
            const label = labelOf(entry.zone) + (missing ? " (not installed)" : "");
            const lastKnown = !mix.some((other, at) => at !== index && known(other.zone));
            return <div key={entry.zone} className={styles.mixEntry}>
                <Picker options={zones.filter(zone => zone.name === entry.zone || !mix.some(other => other.zone === zone.name))}
                    value={entry.zone} disabled={disabled} onPick={zone => replace({ zone })}>
                    <Button className={styles.remove} label={`Remove ${label}`} disabled={disabled || mix.length === 1 || lastKnown}
                        onSelect={() => send(mix.filter((_, at) => at !== index))}>
                        <img className={styles.closeIcon} src={closeIcon} />
                    </Button>
                </Picker>
                <div className={styles.row}>
                    {/* ponytail: shares start at 5, because the tool drops a row whose share is 0; the remove button covers "none". */}
                    <NumberControl label={`${label} share`} value={entry.share} minimum={5} maximum={100} step={5}
                        format={value => String(Math.round(value))} disabled={disabled || missing} onCommit={share => replace({ share })} />
                    <div className={styles.shareOfTotal}>{Math.round(100 * entry.share / total)}%</div>
                </div>
            </div>;
        })}
        {unused ? <Button className={styles.addZone} disabled={disabled} onSelect={() => send([...mix, { zone: unused.name, share: 25 }])}>
            <img className={styles.buttonIcon} src={plusIcon} /><div>Mix in another zone</div>
        </Button> : null}
    </>;
}

// The game's font has no superscript digits: the 2 of km2 and the 3 of m3 are drawn smaller and raised instead.
function Measure({ text, className }: { text: string; className?: string }) {
    const power = /^(.*[a-z])([23])$/.exec(text);
    return <div className={classes(styles.measure, className)}>
        <div>{power ? power[1] : text}</div>
        {power ? <div className={styles.power}>{power[2]}</div> : null}
    </div>;
}

function Stat({ label, value }: { label: string; value: string }) {
    return <div className={styles.stat}>
        <div className={styles.statLabel}>{label}</div>
        <Measure className={styles.statValue} text={value} />
    </div>;
}

const STYLES: [NeighbourhoodStyle, string][] = [["ContourSuburb", "Contour suburb"], ["TerracedBlocks", "Terraced blocks"], ["MountainLane", "Mountain lane"]];
const MIX_PATTERNS: [MixPattern, string][] = [["Random", "Random"], ["Clumps", "Clumps"], ["Even", "Even"]];
const SPACINGS: [string, string][] = [["-1", "Auto"], ["0", "0"], ["1", "1"], ["2", "2"]];

const cells = (value: number) => `${Math.round(value)} ${Math.round(value) === 1 ? "cell" : "cells"}`;
const percent = (value: number) => `${Math.round(value)}%`;
const metres = (value: number) => `${Math.round(value)} m`;
const tenthMetres = (value: number) => `${value.toFixed(1)} m`;
const lots = (count: number) => `${thousands(count)} ${count === 1 ? "lot" : "lots"}`;

function statusTone(state: ToolState) {
    if (state.stage === "Failed") return styles.toneBad;
    if (state.stage === "Building" || state.stage === "Planning") return styles.toneBusy;
    if (state.stage === "Planned" || state.stage === "Built") return styles.toneGood;
    return styles.toneIdle;
}

function NeighbourhoodPanel() {
    const state = useToolState();
    const [confirmedPlan, setConfirmedPlan] = useState<ToolState | null>(null);
    if (state.stage === "Closed") return null;
    const { settings, stats } = state;
    const confirming = state.stage === "Planned" && confirmedPlan === state;
    const building = state.stage === "Building";
    const disabled = building || confirming;
    const hasPlan = (state.stage === "Planned" || state.stage === "Built" || state.stage === "Failed" || building)
        && (stats.lots > 0 || stats.roadKilometres > 0);
    // The tool also lists its summary among the messages; showing it twice reads as noise.
    const messages = state.messages.filter(message => message !== state.summary);
    const zoneLabel = (name: string) => state.zones.find(zone => zone.name === name)?.label ?? name;
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
                        {statusDetail(state) ? <div className={styles.statusDetail}>{statusDetail(state)}</div> : null}
                        {building ? <div className={styles.progressTrack}>
                            <div className={styles.progress} style={{ width: `${100 * state.progress}%` }} />
                        </div> : null}
                        {state.corners >= 3 ? <Measure className={styles.area} text={`Area ${formatArea(state.areaSquareMetres)}`} /> : null}
                        {!building && state.stage !== "Built" ? <div className={styles.hints}>
                            <div>Drag a corner to reshape</div>
                            <div>Right-click removes a corner</div>
                            <div>Ctrl+Z undo</div>
                            <div>Esc goes back</div>
                        </div> : null}
                        {state.corners > 0 ? <div className={styles.statusButtons}>
                            {state.stage === "Drawing" && state.corners >= 3 && !state.areaClosed
                                ? <Button className={styles.statusButton} disabled={disabled} onSelect={() => act("closeArea")}>Close area</Button> : null}
                            <Button className={styles.statusButton} disabled={disabled} onSelect={() => act("undo")}>Undo</Button>
                        </div> : null}
                    </div>

                    <Section title="Style">
                        <div className={styles.row}>
                            <Segmented options={STYLES} value={settings.style} disabled={disabled} onChange={style => act("setStyle", style)} />
                        </div>
                    </Section>

                    <Section title="Roads">
                        <Picker label="Road" options={state.roads} value={settings.road} disabled={disabled} onPick={road => act("setRoad", road)} />
                        <NumberRow label="Block depth" value={settings.blockDepth} minimum={60} maximum={200} step={5}
                            format={metres} trigger="setBlockDepth" disabled={disabled} />
                        <NumberRow label="Max road grade" value={settings.maxRoadGradePercent} minimum={4} maximum={12} step={1}
                            format={percent} trigger="setMaxRoadGrade" disabled={disabled} />
                        <ToggleRow label="Steep hills" value={settings.steepGround} trigger="setSteepGround" disabled={disabled} />
                        <NumberRow label="Steepest buildable" value={settings.steepestBuildablePercent} minimum={8}
                            maximum={settings.steepGround ? 60 : 30} step={1} format={percent} trigger="setSteepestBuildable" disabled={disabled} />
                    </Section>

                    <Section title="Zones">
                        <ZoneMix mix={settings.zoneMix} zones={state.zones} disabled={disabled} />
                        <Row label="Mix mode">
                            <Segmented options={MIX_PATTERNS} value={settings.mixPattern} disabled={disabled}
                                onChange={pattern => act("setMixPattern", pattern)} />
                        </Row>
                        <NumberRow label="Lot width" value={settings.lotWidthCells} minimum={2} maximum={6} step={1}
                            format={cells} trigger="setLotWidth" disabled={disabled} />
                        <NumberRow label="Lot depth" value={settings.lotDepthCells} minimum={2} maximum={6} step={1}
                            format={cells} trigger="setLotDepth" disabled={disabled} />
                    </Section>

                    <Section title="Grading">
                        <ToggleRow label="Level the lots" value={settings.levelLots} trigger="setLevelLots" disabled={disabled} />
                        <NumberRow label="Most earth" value={settings.earthworkLimit} minimum={1} maximum={settings.steepGround ? 12 : 8} step={0.5}
                            format={tenthMetres} trigger="setEarthworkLimit" disabled={disabled} />
                        <NumberRow label="Steepest bank" value={settings.bankGradePercent} minimum={20} maximum={50} step={5}
                            format={percent} trigger="setBankGrade" disabled={disabled} />
                        <Row label="Space between lots">
                            <Segmented options={SPACINGS} value={String(settings.lotSpacing)} disabled={disabled}
                                onChange={spacing => act("setLotSpacing", Number(spacing))} />
                        </Row>
                    </Section>

                    <Section title="Landscape">
                        <ToggleRow label="Retaining walls" value={settings.retainingWalls} trigger="setRetainingWalls" disabled={disabled} />
                        <ToggleRow label="Trees" value={settings.trees} trigger="setTrees" disabled={disabled} />
                        {settings.trees ? <NumberRow label="Tree density" value={settings.treeDensityPercent} minimum={10} maximum={100}
                            step={5} format={percent} trigger="setTreeDensity" disabled={disabled} /> : null}
                    </Section>

                    <div className={styles.seed}>
                        <Row label="Seed">
                            <div className={styles.seedValue}>{settings.seed}</div>
                            <Button disabled={disabled} onSelect={() => act("reroll")}>
                                <img className={styles.buttonIcon} src={diceIcon} /><div>Reroll</div>
                            </Button>
                        </Row>
                    </div>

                    {hasPlan || messages.length > 0 ? <Section title={state.stage === "Built" ? "Result" : "Plan"}>
                        {hasPlan ? <div className={styles.card}>
                            <Stat label="Roads" value={`${stats.roadKilometres.toFixed(1)} km`} />
                            <Stat label="Lots" value={thousands(stats.lots)} />
                            <Stat label="Zoned cells" value={thousands(stats.zonedCells)} />
                            <Stat label="Earth moved" value={`${thousands(stats.earthCubicMetres)} m3`} />
                            <Stat label="Steepest road" value={`${stats.steepestRoadPercent.toFixed(1)}%`} />
                            <Stat label="Forest" value={percent(stats.wildPercent)} />
                            <Stat label="Connected" value={stats.connected ? "Yes" : "No - built on its own"} />
                            <Stat label="Trees" value={thousands(stats.trees)} />
                        </div> : null}
                        {hasPlan ? state.zoneCounts.map(count => <div key={count.zone} className={styles.zoneCount}>
                            <div className={styles.zoneDot} style={{ backgroundColor: zoneColour(count.zone) }} />
                            <div className={styles.zoneName}>{zoneLabel(count.zone)}</div>
                            <div className={styles.zoneLots}>{lots(count.lots)}</div>
                        </div>) : null}
                        {messages.map((message, index) => <div key={index} className={styles.message}>
                            <div className={styles.messageDot} />
                            <div className={styles.messageText}>{message}</div>
                        </div>)}
                    </Section> : null}
                </div>

                <div className={styles.footer}>
                    <Button className={styles.secondary} disabled={disabled || state.corners === 0}
                        onSelect={() => { setConfirmedPlan(null); act("clearArea"); }}>Clear</Button>
                    <Button className={styles.primary} disabled={disabled || state.stage !== "Planned"}
                        onSelect={() => setConfirmedPlan(state)}>{buildLabel}</Button>
                </div>

                {confirming ? <div className={styles.backdrop}>
                    <div className={styles.dialog} role="dialog" aria-modal="true" aria-labelledby="stepped-streets-confirm">
                        <div className={styles.dialogTitle}>Build this neighbourhood?</div>
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
    return <ErrorBoundary name="panel"><NeighbourhoodPanel /></ErrorBoundary>;
}
