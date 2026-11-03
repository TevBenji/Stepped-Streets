// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
// The layout follows Placemaker's panel (jackwrichards, GPL-3.0): docked on the right, small-caps sections, label-left
// rows. The look is the game's own: its panel colours, its green primary pill, tick boxes and pill-shaped choices.
// Plain elements rather than cs2/ui components, so nothing depends on the vanilla themes, which left the first version
// unstyled; the game's slider and text box are used where the registry has them.
import React, { useEffect, useRef, useState } from "react";
import { act, NumberTrigger, playSound, ToggleTrigger, useToolState } from "./lib/bindings";
import { ErrorBoundary } from "./lib/errorBoundary";
import { SettingTip } from "./lib/tips";
import {
    formatArea, MixPattern, NeighbourhoodStyle, Option, ServiceCount, ServiceOption, statusDetail, statusLine, thousands, ToolState, zoneColour, ZoneShare,
} from "./lib/state";
import { vanilla } from "./lib/vanilla";
import icon from "./assets/stepped-streets.svg";
import closeIcon from "./assets/close.svg";
import chevronIcon from "./assets/chevron.svg";
import diceIcon from "./assets/dice.svg";
import circlePlusIcon from "./assets/circle-plus.svg";
import circleGoIcon from "./assets/circle-go.svg";
import checkIcon from "./assets/check.svg";
import undoIcon from "./assets/undo.svg";
import trashIcon from "./assets/trash.svg";
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
    return <SettingTip label={label}><div className={styles.row}>
        <div className={styles.label}>{label}</div>
        <div className={styles.editor}>{children}</div>
    </div></SettingTip>;
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
            <div className={classes(styles.toggleTrack, value && styles.toggleOn)}>
                {value ? <img className={styles.toggleTick} src={checkIcon} /> : null}
            </div>
        </Button>
    </Row>;
}

/** A button that opens an inline list. Without a label it spans the row, and children sit beside it. */
function Picker({ label, options, value, disabled, onPick, initiallyOpen = false, children }: React.PropsWithChildren<{
    label?: string; options: Option[]; value: string; disabled: boolean; onPick: (name: string) => void; initiallyOpen?: boolean;
}>) {
    // A row just added opens its list at once, so adding is one click to choose rather than two.
    const [open, setOpen] = useState(initiallyOpen);
    const [query, setQuery] = useState("");
    // Disabling closes the list, so it doesn't pop back open after the build or the confirmation.
    useEffect(() => { if (disabled) setOpen(false); }, [disabled]);
    useEffect(() => { if (!open) setQuery(""); }, [open]);
    // A saved choice the game no longer lists still shows, so the button never reads blank.
    const shown = options.some(option => option.name === value) ? options : [{ name: value, label: value }, ...options];
    const chosen = shown.find(option => option.name === value);
    const current = chosen?.label ?? value;
    // A long list (zones from other mods, every service building) gets a search box at its top.
    const searchable = vanilla.TextInput !== undefined && shown.length > SEARCH_FROM;
    const needle = query.trim().toLowerCase();
    const listed = needle ? shown.filter(option => option.label.toLowerCase().includes(needle)) : shown;
    // An inline list instead of a popup: the panel scrolls, and a popup would be clipped by it.
    return <>
        <SettingTip label={label}><div className={styles.row}>
            {label ? <div className={styles.label}>{label}</div> : null}
            <Button className={styles.picker} disabled={disabled} selected={open} onSelect={() => setOpen(!open)}>
                {chosen?.icon ? <img className={styles.optionIcon} src={chosen.icon} /> : null}
                <div className={styles.pickerLabel}>{current}</div>
                <img className={classes(styles.chevron, open && styles.chevronOpen)} src={chevronIcon} />
            </Button>
            {children}
        </div></SettingTip>
        {open && !disabled ? <div className={classes(styles.list, !label && styles.listFull)}>
            {searchable && vanilla.TextInput ? <vanilla.TextInput className={styles.search} value={query} placeholder="Search"
                onChange={event => setQuery(event.target.value)} /> : null}
            {listed.map(option => <Button key={option.name} className={styles.listItem} selected={option.name === value}
                onSelect={() => { setOpen(false); if (option.name !== value) onPick(option.name); }}>
                {option.icon ? <img className={styles.optionIcon} src={option.icon} /> : null}<div>{option.label}</div>
            </Button>)}
            {listed.length === 0 ? <div className={styles.noMatch}>Nothing matches "{query.trim()}"</div> : null}
        </div> : null}
    </>;
}

const SEARCH_FROM = 6;

/** What to build: zones with their shares and service buildings with their counts, in one list. */
function Mix({ mix, zones, services, serviceOptions, disabled }: {
    mix: ZoneShare[]; zones: Option[]; services: ServiceCount[]; serviceOptions: ServiceOption[]; disabled: boolean;
}) {
    const [justAdded, setJustAdded] = useState<string | null>(null);
    const sendServices = (next: ServiceCount[]) => act("setServiceMix", JSON.stringify(next.map(({ name, count }) => ({ name, count }))));
    const serviceLabel = (name: string) => serviceOptions.find(option => option.name === name)?.label ?? name;
    const unusedService = serviceOptions.find(option => !services.some(entry => entry.name === option.name));
    const unusedZone = zones.find(zone => !mix.some(entry => entry.zone === zone.name));
    return <>
        <ZoneMix mix={mix} zones={zones} disabled={disabled} justAdded={justAdded} />
        {services.map((entry, index) => {
            const replace = (change: Partial<ServiceCount>) => sendServices(services.map((other, at) => at === index ? { ...other, ...change } : other));
            const label = serviceLabel(entry.name);
            const options = serviceOptions.filter(option => option.name === entry.name || !services.some(other => other.name === option.name))
                .map(option => ({ name: option.name, label: option.group ? `${option.label} (${option.group})` : option.label, icon: option.icon }));
            return <div key={entry.name} className={styles.mixEntry}>
                <Picker options={options} value={entry.name} disabled={disabled} initiallyOpen={entry.name === justAdded}
                    onPick={name => { setJustAdded(null); replace({ name }); }}>
                    <Button className={styles.remove} label={`Remove ${label}`} disabled={disabled}
                        onSelect={() => sendServices(services.filter((_, at) => at !== index))}>
                        <img className={styles.closeIcon} src={closeIcon} />
                    </Button>
                </Picker>
                <div className={styles.row}>
                    <div className={styles.countLabel}>How many</div>
                    <div className={styles.countBox}>
                        <Button className={styles.countButton} label={`Fewer ${label}`} disabled={disabled || entry.count <= 1}
                            onSelect={() => replace({ count: entry.count - 1 })}>-</Button>
                        <div className={styles.countValue}>{entry.count}</div>
                        <Button className={styles.countButton} label={`More ${label}`} disabled={disabled || entry.count >= 20}
                            onSelect={() => replace({ count: entry.count + 1 })}>+</Button>
                    </div>
                </div>
            </div>;
        })}
        <div className={styles.addRow}>
            {unusedZone ? <Button className={styles.addZone} disabled={disabled} onSelect={() => {
                setJustAdded(unusedZone.name);
                act("setZoneMix", JSON.stringify([...mix, { zone: unusedZone.name, share: 25 }]));
            }}><div>Add a zone</div><img className={styles.pillIcon} src={circlePlusIcon} /></Button> : null}
            {unusedService ? <Button className={styles.addZone} disabled={disabled} onSelect={() => {
                setJustAdded(unusedService.name);
                sendServices([...services, { name: unusedService.name, count: 1 }]);
            }}><div>Add a service</div><img className={styles.pillIcon} src={circlePlusIcon} /></Button> : null}
        </div>
    </>;
}

function ZoneMix({ mix, zones, disabled, justAdded }: { mix: ZoneShare[]; zones: Option[]; disabled: boolean; justAdded: string | null }) {
    // The tool takes the whole mix at once, so every edit sends the full new list.
    const send = (next: ZoneShare[]) => act("setZoneMix", JSON.stringify(next.map(({ zone, share }) => ({ zone, share }))));
    const total = mix.reduce((sum, entry) => sum + entry.share, 0);
    const labelOf = (name: string) => zones.find(zone => zone.name === name)?.label ?? name;
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
                    value={entry.zone} disabled={disabled} initiallyOpen={entry.zone === justAdded} onPick={zone => replace({ zone })}>
                    <Button className={styles.remove} label={`Remove ${label}`} disabled={disabled || mix.length === 1 || lastKnown}
                        onSelect={() => send(mix.filter((_, at) => at !== index))}>
                        <img className={styles.closeIcon} src={closeIcon} />
                    </Button>
                </Picker>
                <div className={styles.row}>
                    {/* ponytail: shares start at 5, because the tool drops a row whose share is 0; the remove button covers "none". */}
                    <NumberControl label={`${label} share`} value={entry.share} minimum={5} maximum={100} step={5}
                        format={value => `${Math.round(100 * value / (total - entry.share + value))}%`}
                        disabled={disabled || missing} onCommit={share => replace({ share })} />
                </div>
            </div>;
        })}
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

const STEPS = ["Draw", "Choose", "Build"];

/** Where the player is: draw the area, choose what to build, build. A finished step shows a tick. */
function Steps({ state }: { state: ToolState }) {
    const current = state.stage === "Building" || state.stage === "Built" ? 2 : state.areaClosed ? 1 : 0;
    const finished = state.stage === "Built";
    return <div className={styles.steps}>
        {STEPS.map((name, index) => {
            const done = index < current || finished;
            return <React.Fragment key={name}>
                {index > 0 ? <div className={styles.stepLine} /> : null}
                <div className={classes(styles.step, done ? styles.stepDone : index === current && styles.stepCurrent)}>
                    <div className={styles.stepNumber}>{done ? <img className={styles.stepTick} src={checkIcon} /> : index + 1}</div>
                    <div>{name}</div>
                </div>
            </React.Fragment>;
        })}
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

// The fine settings stay folded away unless the player opened them last time.
let moreOpenMemory = false;

function NeighbourhoodPanel() {
    const state = useToolState();
    const [confirmedPlan, setConfirmedPlan] = useState<ToolState | null>(null);
    const [moreOpen, setMoreOpen] = useState(moreOpenMemory);
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
    const serviceLabel = (name: string) => state.services.find(service => service.name === name)?.label ?? name;
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
                    <Steps state={state} />
                    <div className={classes(styles.status, statusTone(state))} aria-live="polite">
                        <div className={styles.statusHead}>
                            {statusTone(state) === styles.toneGood ? <div className={styles.statusTick}><img className={styles.stepTick} src={checkIcon} /></div> : null}
                            <div className={styles.statusText}>{statusLine(state)}</div>
                            {state.corners >= 3 ? <Measure className={styles.areaChip} text={formatArea(state.areaSquareMetres)} /> : null}
                        </div>
                        {statusDetail(state) ? <div className={styles.statusDetail}>{statusDetail(state)}</div> : null}
                        {building ? <div className={styles.progressTrack}>
                            <div className={styles.progress} style={{ width: `${100 * state.progress}%` }} />
                        </div> : null}
                        {!building && state.stage !== "Built" ? <div className={styles.hints}>
                            <div>{state.areaClosed ? "Drag corners to reshape. Right-click to remove." : "Click the ground to add corners. Click the first one to finish."}</div>
                            <div>Ctrl+Z to undo · Esc to go back</div>
                        </div> : null}
                        {state.corners > 0 ? <div className={styles.statusButtons}>
                            {state.stage === "Drawing" && state.corners >= 3 && !state.areaClosed
                                ? <Button className={styles.statusButton} disabled={disabled} onSelect={() => act("closeArea")}>Close area</Button> : null}
                            <Button className={styles.statusButton} disabled={disabled} onSelect={() => act("undo")}>
                                <img className={styles.buttonIcon} src={undoIcon} /><div>Undo</div>
                            </Button>
                        </div> : null}
                    </div>

                    <Section title="Style">
                        <SettingTip label="Style"><div className={styles.row}>
                            <Segmented options={STYLES} value={settings.style} disabled={disabled} onChange={style => act("setStyle", style)} />
                        </div></SettingTip>
                    </Section>

                    <Section title="What to build">
                        <Mix mix={settings.zoneMix} zones={state.zones} services={settings.serviceMix} serviceOptions={state.services} disabled={disabled} />
                        {settings.zoneMix.length > 1 ? <Row label="Mix mode">
                            <Segmented options={MIX_PATTERNS} value={settings.mixPattern} disabled={disabled}
                                onChange={pattern => act("setMixPattern", pattern)} />
                        </Row> : null}
                    </Section>

                    <Section title="Ground">
                        <ToggleRow label="Steep hills" value={settings.steepGround} trigger="setSteepGround" disabled={disabled} />
                        <ToggleRow label="Trees" value={settings.trees} trigger="setTrees" disabled={disabled} />
                    </Section>

                    <Button className={styles.moreButton} selected={moreOpen} onSelect={() => { moreOpenMemory = !moreOpen; setMoreOpen(!moreOpen); }}>
                        <div className={styles.pickerLabel}>{moreOpen ? "Fewer settings" : "More settings"}</div>
                        <img className={classes(styles.chevron, moreOpen && styles.chevronOpen)} src={chevronIcon} />
                    </Button>

                    {moreOpen ? <>
                        <Section title="Roads">
                            <Picker label="Road" options={state.roads} value={settings.road} disabled={disabled} onPick={road => act("setRoad", road)} />
                            <NumberRow label="Block depth" value={settings.blockDepth} minimum={60} maximum={200} step={5}
                                format={metres} trigger="setBlockDepth" disabled={disabled} />
                            <NumberRow label="Max road grade" value={settings.maxRoadGradePercent} minimum={4} maximum={12} step={1}
                                format={percent} trigger="setMaxRoadGrade" disabled={disabled} />
                            <NumberRow label="Steepest buildable" value={settings.steepestBuildablePercent} minimum={8}
                                maximum={settings.steepGround ? 60 : 30} step={1} format={percent} trigger="setSteepestBuildable" disabled={disabled} />
                        </Section>

                        <Section title="Lots">
                            <NumberRow label="Lot width" value={settings.lotWidthCells} minimum={2} maximum={6} step={1}
                                format={cells} trigger="setLotWidth" disabled={disabled} />
                            <NumberRow label="Lot depth" value={settings.lotDepthCells} minimum={2} maximum={6} step={1}
                                format={cells} trigger="setLotDepth" disabled={disabled} />
                            <Row label="Space between lots">
                                <Segmented options={SPACINGS} value={String(settings.lotSpacing)} disabled={disabled}
                                    onChange={spacing => act("setLotSpacing", Number(spacing))} />
                            </Row>
                        </Section>

                        <Section title="Grading">
                            <ToggleRow label="Level the lots" value={settings.levelLots} trigger="setLevelLots" disabled={disabled} />
                            <NumberRow label="Most earth" value={settings.earthworkLimit} minimum={1} maximum={settings.steepGround ? 12 : 8} step={0.5}
                                format={tenthMetres} trigger="setEarthworkLimit" disabled={disabled} />
                            <NumberRow label="Steepest bank" value={settings.bankGradePercent} minimum={20} maximum={50} step={5}
                                format={percent} trigger="setBankGrade" disabled={disabled} />
                        </Section>

                        <Section title="Landscape">
                            <ToggleRow label="Retaining walls" value={settings.retainingWalls} trigger="setRetainingWalls" disabled={disabled} />
                            {settings.trees ? <NumberRow label="Tree density" value={settings.treeDensityPercent} minimum={10} maximum={100}
                                step={5} format={percent} trigger="setTreeDensity" disabled={disabled} /> : null}
                            <Row label="Seed"><div className={styles.seedValue}>{settings.seed}</div></Row>
                        </Section>
                    </> : null}

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
                        {hasPlan ? state.serviceCounts.map(count => <div key={count.name} className={styles.zoneCount}>
                            <div className={classes(styles.zoneDot, styles.serviceDot)} />
                            <div className={styles.zoneName}>{serviceLabel(count.name)}</div>
                            <div className={styles.zoneLots}>{`${count.count} ${count.count === 1 ? "building" : "buildings"}`}</div>
                        </div>) : null}
                        {messages.map((message, index) => <div key={index} className={styles.message}>
                            <div className={styles.messageDot} />
                            <div className={styles.messageText}>{message}</div>
                        </div>)}
                    </Section> : null}
                </div>

                <div className={styles.footer}>
                    <Button className={styles.secondary} disabled={disabled || state.corners === 0}
                        onSelect={() => { setConfirmedPlan(null); act("clearArea"); }}>
                        <img className={styles.buttonIcon} src={trashIcon} /><div>Clear</div>
                    </Button>
                    <Button className={styles.secondary} label="Reroll: another layout" disabled={disabled} onSelect={() => act("reroll")}>
                        <img className={styles.buttonIcon} src={diceIcon} /><div>Reroll</div>
                    </Button>
                    <Button className={styles.primary} disabled={disabled || state.stage !== "Planned"}
                        onSelect={() => setConfirmedPlan(state)}>
                        <div>{buildLabel}</div>
                        {!disabled && state.stage === "Planned" ? <img className={styles.pillIcon} src={circleGoIcon} /> : null}
                    </Button>
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
                            }}><div>Build</div><img className={styles.pillIcon} src={circleGoIcon} /></Button>
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
